import {
  Box3,
  Color,
  Group,
  InstancedMesh,
  type Material,
  Matrix4,
  type Mesh,
  type MeshStandardMaterial,
  type Object3D,
  Quaternion,
  Vector3,
} from 'three';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { holdFirstFrame } from './look.js';

/**
 * The nature models in `public/models/nature/`: trees, bushes, flower clumps, rocks and a fern from Quaternius's
 * Stylized Nature MegaKit (CC0, see NOTICE), compressed for a headset (meshopt geometry, 512 px WebP textures, about
 * 0.65 MB for all eleven). They load once from the app's own files, never from the network, and the first frame waits
 * for them, so nothing pops in. The textures are files of their own beside `nature.gltf`, not packed into one binary:
 * the page's security policy lets it fetch its own files but not the blob: addresses a packed file's pictures load from.
 */
export type NatureModel =
  | 'CommonTree_1'
  | 'CommonTree_2'
  | 'CommonTree_5'
  | 'Bush_Common'
  | 'Bush_Common_Flowers'
  | 'Flower_3_Group'
  | 'Flower_4_Group'
  | 'Rock_Medium_1'
  | 'Rock_Medium_2'
  | 'Rock_Medium_3'
  | 'Fern_1';

interface Loaded {
  readonly model: Object3D;
  readonly height: number;
}

let kit: Promise<Map<string, Loaded> | null> | null = null;

/** Loads the models once, each ready to place: casting and taking shadows, matte, its height measured. */
function natureKit(): Promise<Map<string, Loaded> | null> {
  if (kit === null) {
    kit = new GLTFLoader()
      .setMeshoptDecoder(MeshoptDecoder)
      .loadAsync(`${import.meta.env.BASE_URL}models/nature/nature.gltf`)
      .then(
        (gltf) => {
          const models = new Map<string, Loaded>();
          for (const scene of gltf.scenes)
            for (const model of scene.children) {
              model.traverse((object) => {
                const mesh = object as Mesh;
                if (mesh.isMesh !== true) return;
                mesh.castShadow = true;
                mesh.receiveShadow = true;
                const material = mesh.material as MeshStandardMaterial;
                // Matte, as leaves, bark and stone are; the leaves stay cut out of their cards (alphaTest), never blended.
                material.roughness = 0.85;
                material.metalness = 0;
                // The kit's vertex colours weight its wind for the maker's own shader, not its colour: its textures are.
                material.vertexColors = false;
              });
              models.set(model.name, { model, height: new Box3().setFromObject(model).max.y });
            }
          return models;
        },
        () => null,
      );
    holdFirstFrame(kit);
  }
  return kit;
}

/**
 * A nature model, `height` metres tall from the ground, in a group you can place, turn and sway at once; the model
 * fills it as soon as the kit has loaded (before the first frame). `tint` multiplies its colours, for a world's own
 * stone or leaf. If the file cannot be read, `fallback` fills it instead, or nothing does.
 */
export function natureModel(name: NatureModel, height: number, look: { tint?: string; fallback?: () => Object3D } = {}): Group {
  const slot = new Group();
  void natureKit().then((models) => {
    const loaded = models?.get(name);
    if (loaded === undefined) {
      if (look.fallback !== undefined) slot.add(look.fallback());
      return;
    }
    // The model keeps its own placement (compression moves and scales each one), inside a group scaled to the height.
    const copy = new Group();
    copy.add(loaded.model.clone());
    copy.scale.setScalar(height / loaded.height);
    if (look.tint !== undefined) {
      const tint = new Color(look.tint);
      copy.traverse((object) => {
        const mesh = object as Mesh;
        if (mesh.isMesh !== true) return;
        const material = (mesh.material as Material).clone() as MeshStandardMaterial;
        material.color.multiply(tint);
        mesh.material = material;
      });
    }
    slot.add(copy);
  });
  return slot;
}

/**
 * Many of one nature model drawn at once, one draw call for each of its parts (bark, leaves): at each place
 * (x, y, z, size, turn), `height * size` metres tall. For the land beyond the island, where dozens stand; they cast no
 * shadow there (the sun's shadow covers only the island), so give each a contact shadow of its own. `tint`
 * multiplies their colours, for a world's own light. If the file cannot be read the group stays empty.
 */
export function natureInstances(
  name: NatureModel,
  height: number,
  places: readonly (readonly [number, number, number, number, number])[],
  tint?: string,
): Group {
  const group = new Group();
  void natureKit().then((models) => {
    const loaded = models?.get(name);
    if (loaded === undefined || places.length === 0) return;
    loaded.model.updateWorldMatrix(true, true);
    const place = new Matrix4();
    const turn = new Quaternion();
    const up = new Vector3(0, 1, 0);
    loaded.model.traverse((object) => {
      const mesh = object as Mesh;
      if (mesh.isMesh !== true) return;
      let material = mesh.material as Material;
      if (tint !== undefined) {
        const tinted = material.clone() as MeshStandardMaterial;
        tinted.color.multiply(new Color(tint));
        material = tinted;
      }
      const many = new InstancedMesh(mesh.geometry, material, places.length);
      for (const [k, [x, y, z, size, angle]] of places.entries()) {
        const scale = (height * size) / loaded.height;
        place.compose(new Vector3(x, y, z), turn.setFromAxisAngle(up, angle), new Vector3(scale, scale, scale));
        many.setMatrixAt(k, place.multiply(mesh.matrixWorld));
      }
      many.instanceMatrix.needsUpdate = true;
      many.computeBoundingSphere();
      many.receiveShadow = true;
      group.add(many);
    });
  });
  return group;
}
