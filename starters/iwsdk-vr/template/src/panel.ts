/**
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */

import { createSystem, UIKitMLAsset, VisibilityState } from '@iwsdk/core';
import { CARD_SECONDS, cardOpacity } from './card-fade.js';
import { helpPressed } from './play-input.js';

/*
 * The card's words stay CARD_SECONDS of game time, fading in and out by game time (card-fade.ts), never a timer or a
 * press: a checked page then draws the same frames in every load, and a press is seen only when the game itself
 * answers it. On a laptop H shows them again for as long (`helpPressed`); in a headset they stay gone.
 */

export class PanelSystem extends createSystem({}) {
  #wordsShown = true;
  #shownAt = 0;
  /** What fades: the whole card where nothing of it stays once its words go, else only the words. */
  #fade: ((opacity: number) => void) | null = null;
  #dropWords: (() => void) | null = null;
  #showWords: (() => void) | null = null;

  init(): void {
    const panel = this.world.getSceneObject<UIKitMLAsset>('welcome-panel');
    const card = panel?.getElementById('card');
    const words = panel?.getElementById('card-words');
    const xrButton = panel?.getElementById('xr-button');
    const exitButton = panel?.getElementById('exit-button');
    const laptopHelp = panel?.getElementById('laptop-help');
    const helpAgain = panel?.getElementById('help-again');
    if (xrButton == null || exitButton == null) {
      return;
    }
    if (!this.world.xrEnabled) {
      xrButton.setProperties({ display: 'none' });
      exitButton.setProperties({ display: 'none' });
      laptopHelp?.setProperties({ display: 'flex' });
      helpAgain?.setProperties({ display: 'flex' });
      // Nothing of the card is left to show once its words go; H brings it back.
      this.#fade = (opacity) => card?.setProperties({ opacity });
      this.#dropWords = () => card?.setProperties({ display: 'none' });
      this.#showWords = () => card?.setProperties({ display: 'flex', opacity: 0 });
      return;
    }
    // A laptop's browser cannot start VR: it says how to play there instead of showing a button that does nothing.
    let vrSupported = false;
    const show = (visibilityState: VisibilityState) => {
      const is2D = visibilityState === VisibilityState.NonImmersive;
      xrButton.setProperties({ display: is2D && vrSupported ? 'flex' : 'none' });
      const laptop = is2D && !vrSupported && this.#wordsShown ? 'flex' : 'none';
      laptopHelp?.setProperties({ display: laptop });
      helpAgain?.setProperties({ display: laptop });
      exitButton.setProperties({ display: is2D ? 'none' : 'flex' });
      // Once the words go, the card stays only as a button: Enter VR on a headset's page, Exit to Browser in VR.
      const button = (is2D && vrSupported) || !is2D;
      card?.setProperties({ display: this.#wordsShown || button ? 'flex' : 'none' });
      this.#fade = button
        ? (opacity) => words?.setProperties({ opacity })
        : (opacity) => card?.setProperties({ opacity });
    };
    this.#dropWords = () => {
      words?.setProperties({ display: 'none' });
      show(this.world.visibilityState.value);
    };
    this.#showWords = () => {
      words?.setProperties({ display: 'flex', opacity: 1 });
      card?.setProperties({ opacity: 1 });
      show(this.world.visibilityState.value);
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

  update(_delta: number, time: number): void {
    const immersive = this.world.visibilityState.value !== VisibilityState.NonImmersive;
    // On a laptop, H shows the words again for another CARD_SECONDS of game time, fading in.
    if (!immersive && !this.#wordsShown && this.#showWords !== null && helpPressed()) {
      this.#wordsShown = true;
      this.#shownAt = time;
      this.#showWords();
    }
    if (!this.#wordsShown || this.#dropWords === null) return;
    if (!immersive) {
      this.#fade?.(cardOpacity(time, this.#shownAt));
      // Gone only once fully faded: never a cut.
      if (time < this.#shownAt + CARD_SECONDS) return;
    }
    this.#wordsShown = false;
    this.#dropWords();
  }
}
