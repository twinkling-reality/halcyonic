package com.halcyonic.panel;

/**
 * Present only in development builds (PanelInBuilds copies this folder only when GlanceInDevelopmentBuilds.Include is set):
 * PanelCapture finds it to know the build is a development one. A release APK never packages it, so its panel stays secure.
 */
final class PanelCaptureDevelopment {
    private PanelCaptureDevelopment() {}
}
