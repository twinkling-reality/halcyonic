import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  deviceMillis,
  epochMillis,
  isStageReady,
  median,
  readBattery,
  readCompositor,
  readFirstFrame,
  readFrames,
  readLaunch,
  readThermal,
  readViewField,
} from './readings.ts';

describe('device readings', () => {
  test('the battery reads as a percent, degrees and whether it charges', () => {
    const text = [
      'Current Battery Service state:',
      '  AC powered: false',
      '  USB powered: true',
      '  Wireless powered: false',
      '  status: 2',
      '  level: 87',
      '  scale: 100',
      '  voltage: 4123',
      '  temperature: 312',
    ].join('\n');
    assert.deepEqual(readBattery(text), { levelPercent: 87, celsius: 31.2, plugged: true });
    assert.deepEqual(readBattery('nothing here'), {
      levelPercent: null,
      celsius: null,
      plugged: null,
    });
  });

  test('the thermal service gives its status and the hottest sensor of each type, from the HAL first', () => {
    const text = [
      'IsStatusOverride: false',
      'Thermal Status: 1',
      'Cached temperatures:',
      '\tTemperature{mValue=90.0, mType=0, mName=cpu0, mStatus=0}',
      'HAL Ready: true',
      'Current temperatures from HAL:',
      '\tTemperature{mValue=41.5, mType=0, mName=cpu0, mStatus=0}',
      '\tTemperature{mValue=44.25, mType=0, mName=cpu1, mStatus=0}',
      '\tTemperature{mValue=39.0, mType=1, mName=gpu, mStatus=0}',
      '\tTemperature{mValue=33.1, mType=3, mName=skin, mStatus=0}',
    ].join('\n');
    const reading = readThermal(text);
    assert.equal(reading.status, 1);
    assert.equal(reading.hottestByType.get(0), 44.25);
    assert.equal(reading.hottestByType.get(1), 39);
    assert.equal(reading.hottestByType.get(3), 33.1);
    const cachedOnly = readThermal(text.split('HAL Ready')[0] ?? '');
    assert.equal(cachedOnly.hottestByType.get(0), 90);
  });

  test("the compositor's line gives its frame rate, target, stale frames and temperature", () => {
    const line =
      '1696234567.123  1234  1256 I VrApi   : FPS=71/72,Prd=45ms,Tear=0,Early=0,Stale=2,Stale2/5/10/max=0/0/0/0,VSnc=1,Lat=-1,Fov=0,CPU4/GPU=4/4,1651/490MHz,OC=FF,TA=0/0/0,SP=N/N/N,Mem=2092MHz,Free=2010MB,PLS=0,Temp=34.0C/0.0C,TW=2.86ms,App=5.95ms';
    assert.deepEqual(readCompositor(line), { fps: 71, target: 72, stale: 2, celsius: 34 });
    assert.equal(readCompositor('I Unity: Halcyonic: connection Live'), null);
  });

  test("Halcyonic's own frame line reads back as numbers", () => {
    const line =
      '1696234567.123  1234  1256 I Unity   : Halcyonic: device frames 4320 in 60.0 s, 72.0 a second at 72 Hz, slowest 18.4 ms, 3 below 60, 5 missed';
    assert.deepEqual(readFrames(line), {
      frames: 4320,
      seconds: 60,
      perSecond: 72,
      hertz: 72,
      slowestMs: 18.4,
      belowSixty: 3,
      missed: 5,
    });
    assert.deepEqual(
      readFrames(
        'Halcyonic: device frames 10 in 0.2 s, 50.0 a second, slowest 20.0 ms, 10 below 60, ended by a pause',
      ),
      {
        frames: 10,
        seconds: 0.2,
        perSecond: 50,
        hertz: null,
        slowestMs: 20,
        belowSixty: 10,
        missed: null,
      },
    );
  });

  test('the first frame, the field of view and the stage being ready are found', () => {
    assert.equal(
      readFirstFrame('I Unity   : Halcyonic: device first frame 2345 ms after start'),
      2345,
    );
    assert.deepEqual(
      readViewField(
        'Halcyonic: device view field left eye left 52.0 right 43.0 up 48.0 down 50.0, right eye left 43.0 right 52.0 up 48.0 down 50.0, both 104.0 across 96.0 tall',
      ),
      { across: 104, tall: 96 },
    );
    assert.equal(isStageReady('Halcyonic: demonstration plays from its beginning (1)'), true);
    assert.equal(isStageReady('Halcyonic: demonstration plays from its beginning (2)'), false);
    assert.equal(isStageReady('Halcyonic: connection Live'), true);
    assert.equal(isStageReady('Halcyonic: connection Connecting'), false);
  });

  test('times come from the headset clock', () => {
    assert.equal(epochMillis('1696234567.123  1234  1256 I Unity : x'), 1_696_234_567_123);
    assert.equal(epochMillis('--------- beginning of main'), null);
    assert.equal(deviceMillis('1696234567.123456789\n'), 1_696_234_567_123);
    assert.equal(deviceMillis('1696234567.N\n'), 1_696_234_567_000);
    assert.throws(() => deviceMillis('date: unknown option'));
    assert.deepEqual(
      readLaunch(
        'Starting: Intent { cmp=x }\nStatus: ok\nLaunchState: COLD\nTotalTime: 1840\nWaitTime: 1852\nComplete',
      ),
      { totalMs: 1840, waitMs: 1852 },
    );
  });

  test('the median of a few runs', () => {
    assert.equal(median([]), null);
    assert.equal(median([3, 1, 2]), 2);
    assert.equal(median([4, 1, 3, 2]), 2.5);
  });
});
