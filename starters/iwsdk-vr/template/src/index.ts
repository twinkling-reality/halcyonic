/**
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */

import { World } from '@iwsdk/core';
import projectOptions from 'virtual:iwsdk-project';
import { decorate } from './decor.js';
import { gameIsland, setupLook } from './look.js';
import { PanelSystem } from './panel.js';
import { setupPlayInput } from './play-input.js';

World.create(
  document.getElementById('scene-container') as HTMLDivElement,
  projectOptions,
).then((world) => {
  setupLook(world);
  decorate(gameIsland());
  setupPlayInput(world);
  world.registerSystem(PanelSystem);
});
