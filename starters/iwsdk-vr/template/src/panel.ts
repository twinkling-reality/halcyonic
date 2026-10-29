/**
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */

import { createSystem, UIKitMLAsset, VisibilityState } from '@iwsdk/core';

export class PanelSystem extends createSystem({}) {
  init(): void {
    const panel = this.world.getSceneObject<UIKitMLAsset>('welcome-panel');
    const xrButton = panel?.getElementById('xr-button');
    const exitButton = panel?.getElementById('exit-button');
    const laptopHelp = panel?.getElementById('laptop-help');
    if (xrButton == null || exitButton == null) {
      return;
    }
    if (!this.world.xrEnabled) {
      xrButton.setProperties({ display: 'none' });
      exitButton.setProperties({ display: 'none' });
      laptopHelp?.setProperties({ display: 'flex' });
      return;
    }
    // A laptop's browser cannot start VR: it says how to play there instead of showing a button that does nothing.
    let vrSupported = false;
    const show = (visibilityState: VisibilityState) => {
      const is2D = visibilityState === VisibilityState.NonImmersive;
      xrButton.setProperties({ display: is2D && vrSupported ? 'flex' : 'none' });
      laptopHelp?.setProperties({ display: is2D && !vrSupported ? 'flex' : 'none' });
      exitButton.setProperties({ display: is2D ? 'none' : 'flex' });
    };
    void navigator.xr?.isSessionSupported('immersive-vr').then((supported) => {
      vrSupported = supported;
      show(this.world.visibilityState.value);
    });

    const launchXR = () => this.world.launchXR();
    const exitXR = () => this.world.exitXR();
    xrButton.addEventListener('click', launchXR);
    exitButton.addEventListener('click', exitXR);
    this.cleanupFuncs.push(
      () => xrButton.removeEventListener('click', launchXR),
      () => exitButton.removeEventListener('click', exitXR),
      this.world.visibilityState.subscribe(show),
    );
  }
}
