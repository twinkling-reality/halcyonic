import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  type CommandEnvelope,
  type CommandId,
  MEDIA_FOLDER_BYTES,
  MEDIA_PER_PROJECT,
  type MediaType,
  type RuntimeDescriptor,
  type RuntimeId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import { admitCommand, type RuntimeCatalog } from './admission.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

// A project's photos and videos (ADR 0066): what the journal says the host wrote into its folder.

const DIGEST = 'a'.repeat(64);

function runtimes(takesPictures: boolean): RuntimeCatalog {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'mock' as RuntimeId,
    kind: 'mock',
    display_name: 'Mock',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: false,
      respond_to_approval: true,
      answer_question: false,
      interrupt: true,
    },
    model_choice: 'none',
    uses_project_location: false,
    reports_tool_activity: false,
    ...(takesPictures && { takes_pictures: true }),
  };
  return { get: (id) => (id === 'mock' ? descriptor : undefined) };
}

function made(location: boolean = true) {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project(
    'Dunes',
    location ? { path: '/work/dunes', name: 'dunes', created: true } : null,
  );
  projection.apply(project.event);
  const projectId = project.projectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  let counter = 0;
  const envelope = (
    command: Pick<CommandEnvelope, 'command_type' | 'payload'>,
  ): CommandEnvelope => {
    counter += 1;
    return {
      schema_version: 1,
      command_id: `00000000-0000-4000-9000-${String(counter).padStart(12, '0')}` as CommandId,
      issued_at: '2026-11-16T02:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      ...command,
    } as CommandEnvelope;
  };
  const added = (path: string, size_bytes = 1_000, media_type: MediaType = 'video/quicktime') =>
    projection.apply(
      b.controlPlane('project.media.added', scope, {
        command_id: envelope({
          command_type: 'project.media.add',
          payload: { project_id: projectId, media: DIGEST },
        }).command_id,
        path,
        media_type,
        size_bytes,
        sha256: DIGEST,
      }),
    );
  const prepared = (path: string, stills: number, play: boolean) =>
    projection.apply(
      b.controlPlane('project.media.prepared', scope, {
        path,
        stills: Array.from({ length: stills }, (_, index) => ({
          path: `${path}-stills/${index + 1}.jpg`,
          size_bytes: 100,
          sha256: DIGEST,
          at_ms: index * 1_000,
        })),
        stills_not_made: stills === 0 ? 'tool_missing' : null,
        play: play ? { path: `${path}-play.mp4`, size_bytes: 500, sha256: DIGEST } : null,
        play_not_made: play ? null : 'tool_missing',
        width: 1376,
        height: 1840,
        duration_ms: 6_600,
      }),
    );
  const add = () =>
    envelope({
      command_type: 'project.media.add',
      payload: { project_id: projectId, media: DIGEST },
    });
  return { b, projection, projectId, scope, envelope, added, prepared, add };
}

describe("a project's photos and videos in current state (ADR 0066)", () => {
  test('each added one is listed newest first, drawn by its first still once the host made it', () => {
    const { projection, projectId, added, prepared } = made();
    assert.equal(projection.project(projectId)?.media, undefined, 'none added: none listed');
    added('media/video-aaaaaaaaaaaa.mov');
    added('media/photo-bbbbbbbbbbbb.jpg', 2_000, 'image/jpeg');
    assert.deepEqual(
      projection.project(projectId)?.media?.map((medium) => [medium.path, medium.prepared]),
      [
        ['media/photo-bbbbbbbbbbbb.jpg', false],
        ['media/video-aaaaaaaaaaaa.mov', false],
      ],
    );
    prepared('media/video-aaaaaaaaaaaa.mov', 4, true);
    const video = projection.project(projectId)?.media?.[1];
    assert.deepEqual(video, {
      path: 'media/video-aaaaaaaaaaaa.mov',
      media_type: 'video/quicktime',
      size_bytes: 1_000,
      added_at: video?.added_at,
      still: 'media/video-aaaaaaaaaaaa.mov-stills/1.jpg',
      play: 'media/video-aaaaaaaaaaaa.mov-play.mp4',
      duration_ms: 6_600,
      prepared: true,
    });
    // What the host made from it counts toward the folder's bound: 1000 + 4 x 100 + 500, and the photo's 2000.
    assert.equal(projection.mediaBytes(projectId), 3_900);
  });

  test('stills that could not be made leave the medium added and drawn without one', () => {
    const { projection, projectId, added, prepared } = made();
    added('media/video-aaaaaaaaaaaa.mov');
    prepared('media/video-aaaaaaaaaaaa.mov', 0, false);
    const [video] = projection.project(projectId)?.media ?? [];
    assert.equal(video?.still, null);
    assert.equal(video?.play, null);
    assert.equal(video?.prepared, true);
  });

  test('the same path added twice, or prepared before it was added, changes nothing and is noted', () => {
    const { projection, projectId, added, prepared } = made();
    added('media/video-aaaaaaaaaaaa.mov');
    assert.equal(added('media/video-aaaaaaaaaaaa.mov').notes[0]?.code, 'duplicate_entity');
    assert.equal(
      prepared('media/video-cccccccccccc.mov', 1, false).notes[0]?.code,
      'unknown_entity',
    );
    assert.equal(projection.project(projectId)?.media?.length, 1);
  });

  test('they survive a checkpoint of the projection (ADR 0050)', () => {
    const { projection, projectId, added, prepared } = made();
    added('media/video-aaaaaaaaaaaa.mov');
    prepared('media/video-aaaaaaaaaaaa.mov', 2, true);
    const restored = new Projection();
    restored.restoreState(projection.exportState());
    assert.deepEqual(restored.project(projectId)?.media, projection.project(projectId)?.media);
  });
});

describe('admitting a photo or video into a project (ADR 0066)', () => {
  test('it needs the project, its folder, nothing at work in it, and room for one more', () => {
    const { b, projection, projectId, scope, envelope, added, add } = made();
    assert.equal(admitCommand(add(), projection, runtimes(false)).admitted, true);

    const nowhere = made(false);
    const refused = admitCommand(nowhere.add(), nowhere.projection, runtimes(false));
    assert.equal(refused.admitted ? null : refused.rejection.code, 'location_required');

    // A check under way writes the folder: the add waits for it.
    const check = envelope({ command_type: 'project.check', payload: { project_id: projectId } });
    projection.apply(
      b.controlPlane('command.accepted', scope, {
        command: check,
        policy: 'low_consequence',
        received_via: 'internal',
        principal: null,
      }),
    );
    const busy = admitCommand(add(), projection, runtimes(false));
    assert.equal(busy.admitted ? null : busy.rejection.code, 'invalid_state');
    assert.match(busy.admitted ? '' : busy.rejection.message, /so Add Media was not done\./);
    projection.apply(
      b.controlPlane('command.completed', scope, {
        command_id: check.command_id,
        command_type: 'project.check',
        result: null,
      }),
    );
    assert.equal(admitCommand(add(), projection, runtimes(false)).admitted, true);

    for (let index = 0; index < MEDIA_PER_PROJECT; index += 1)
      added(`media/photo-${String(index).padStart(12, '0')}.jpg`, 10, 'image/jpeg');
    const full = admitCommand(add(), projection, runtimes(false));
    assert.deepEqual(full.admitted ? null : full.rejection, {
      code: 'media_full',
      message: `This project holds ${MEDIA_PER_PROJECT} photos and videos, the most it keeps.`,
    });
  });

  test("the folder's bound counts bytes as well as files", () => {
    const { projection, added, add } = made();
    added('media/video-aaaaaaaaaaaa.mov', MEDIA_FOLDER_BYTES);
    const full = admitCommand(add(), projection, runtimes(false));
    assert.equal(full.admitted ? null : full.rejection.code, 'media_full');
    // The number said is the bound in the unit said: MB as a Mac counts them (AUDIT2 M1).
    assert.equal(MEDIA_FOLDER_BYTES, 240 * 1_000_000);
    assert.match(full.admitted ? '' : full.rejection.message, /take 240 MB, the most it keeps\./);
  });
});

describe("an instruction's photos and videos (ADR 0066)", () => {
  function started(takesPictures: boolean) {
    const setup = made();
    const workstream = setup.b.workstream(setup.projectId);
    setup.projection.apply(workstream.event);
    setup.added('media/video-aaaaaaaaaaaa.mov');
    setup.prepared('media/video-aaaaaaaaaaaa.mov', 4, true);
    const start = (attachments: unknown[]) =>
      admitCommand(
        setup.envelope({
          command_type: 'execution.start',
          payload: {
            workstream_id: workstream.workstreamId as WorkstreamId,
            runtime_id: 'mock' as RuntimeId,
            instruction: 'Make a place from this.',
            options: {},
            model_ref: null,
            attachments,
          },
        } as never),
        setup.projection,
        runtimes(takesPictures),
      );
    return { ...setup, workstream, start };
  }
  const video = { kind: 'media', path: 'media/video-aaaaaaaaaaaa.mov' };
  const picture = { kind: 'image', path: 'art/tree.png' };

  test('a medium is one of the project’s own, and its stills count toward the four pictures', () => {
    const { start } = started(true);
    assert.equal(start([video]).admitted, true);
    const unknown = start([{ kind: 'media', path: 'media/video-cccccccccccc.mov' }]);
    assert.deepEqual(unknown.admitted ? null : unknown.rejection, {
      code: 'invalid_state',
      message: "That photo or video is not one of this project's.",
    });
    const over = start([video, picture]);
    assert.equal(over.admitted ? null : over.rejection.code, 'invalid_state');
    assert.match(over.admitted ? '' : over.rejection.message, /at most 4 pictures/);
  });

  test('a photo that is itself a picture is its own one still: counted once, shown on the card (AUDIT3 K1)', () => {
    const { projection, projectId, added, scope, b, start } = started(true);
    const photo = 'media/photo-dddddddddddd.jpg';
    added(photo, 3_000, 'image/jpeg');
    projection.apply(
      b.controlPlane('project.media.prepared', scope, {
        path: photo,
        stills: [{ path: photo, size_bytes: 3_000, sha256: DIGEST, at_ms: 0 }],
        stills_not_made: null,
        play: null,
        play_not_made: null,
        width: 1200,
        height: 900,
        duration_ms: null,
      }),
    );
    assert.equal(projection.project(projectId)?.media?.[0]?.still, photo);
    // The video's 1000 + 4 x 100 + 500, and the photo's 3000 once.
    assert.equal(projection.mediaBytes(projectId), 4_900);
    assert.equal(start([{ kind: 'media', path: photo }]).admitted, true);
    const five = start([picture, picture, picture, picture, { kind: 'media', path: photo }]);
    assert.equal(five.admitted ? null : five.rejection.code, 'invalid_state');
  });

  test('a runtime that takes no pictures takes a medium alone, as the host’s line, never with a picture', () => {
    const { start } = started(false);
    assert.equal(start([video]).admitted, true);
    const withPicture = start([video, picture]);
    assert.equal(
      withPicture.admitted ? null : withPicture.rejection.code,
      'capability_unsupported',
    );
  });

  test('the run shows each medium it was given once, by its first still', () => {
    const { b, projection, projectId, workstream } = started(true);
    const execution = b.execution(projectId, workstream.workstreamId, '/work/dunes');
    projection.apply(execution.event);
    const where = {
      project_id: projectId,
      workstream_id: workstream.workstreamId,
      execution_id: execution.executionId,
    };
    const given = {
      command_id: '00000000-0000-4000-9000-000000000066' as CommandId,
      media: [
        {
          path: 'media/video-aaaaaaaaaaaa.mov',
          media_type: 'video/quicktime' as const,
          stills_given: 4,
        },
      ],
      line_added: true,
    };
    projection.apply(b.controlPlane('execution.instruction.media_given', where, given));
    projection.apply(b.controlPlane('execution.instruction.media_given', where, given));
    assert.deepEqual(projection.execution(execution.executionId)?.media, [
      {
        path: 'media/video-aaaaaaaaaaaa.mov',
        media_type: 'video/quicktime',
        still: 'media/video-aaaaaaaaaaaa.mov-stills/1.jpg',
      },
    ]);
  });
});
