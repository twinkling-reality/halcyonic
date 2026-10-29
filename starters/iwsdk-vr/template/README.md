# VR app

A WebXR app made with the Immersive Web SDK. Halcyonic serves it to your headset's browser and checks it after every
change; this folder needs no install.

- `src/index.ts` starts the world and registers systems.
- `src/assets.ts` names the files the app loads, all from `public/`.
- `public/scenes/main.iwsdk.scene.json` places things in the scene.
- `iwsdk.config.json` holds the scene, XR and feature settings.
