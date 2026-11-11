#nullable enable
using System.Xml;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// The preview relay's place in the library's manifest (ADR 0052, the lead's decision of 2026-11-11): development builds
    /// only. A development build gets the relay's foreground service, <c>PreviewRelayService</c>, not exported, of the
    /// special-use type with its subtype, and the two permissions it needs; any other build carries none of them, removed
    /// again where Unity reused a Gradle project a development build made. No UnityEditor reference, so a test reads it.
    /// </summary>
    internal static class RelayManifest
    {
        internal const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        internal const string Service = "com.halcyonic.relay.PreviewRelayService";

        internal const string ForegroundService = "android.permission.FOREGROUND_SERVICE";

        internal const string SpecialUse = "android.permission.FOREGROUND_SERVICE_SPECIAL_USE";

        internal const string SubtypeProperty = "android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE";

        internal const string Subtype = "Keeps the person's app preview open in the headset's browser, relayed from their own paired computer";

        /// <summary>Puts the relay's service and permissions in <paramref name="document"/> where <paramref name="include"/>, else takes them out.</summary>
        internal static void Apply(XmlDocument document, bool include)
        {
            var manifest = document.DocumentElement!;
            var application = (XmlElement?)manifest.SelectSingleNode("application");
            Remove(manifest, application);
            if (!include || application == null) return;
            var service = document.CreateElement("service");
            service.SetAttribute("name", AndroidNamespace, Service);
            service.SetAttribute("exported", AndroidNamespace, "false");
            service.SetAttribute("foregroundServiceType", AndroidNamespace, "specialUse");
            var property = document.CreateElement("property");
            property.SetAttribute("name", AndroidNamespace, SubtypeProperty);
            property.SetAttribute("value", AndroidNamespace, Subtype);
            service.AppendChild(property);
            application.AppendChild(service);
            foreach (var name in new[] { ForegroundService, SpecialUse })
            {
                var permission = document.CreateElement("uses-permission");
                permission.SetAttribute("name", AndroidNamespace, name);
                manifest.InsertBefore(permission, application);
            }
        }

        /// <summary>Takes out the relay's service and both permissions, wherever an earlier build left them.</summary>
        private static void Remove(XmlElement manifest, XmlElement? application)
        {
            foreach (var element in Named(manifest, "uses-permission"))
            {
                var name = element.GetAttribute("name", AndroidNamespace);
                if (name == ForegroundService || name == SpecialUse) manifest.RemoveChild(element);
            }
            if (application == null) return;
            foreach (var element in Named(application, "service"))
                if (element.GetAttribute("name", AndroidNamespace) == Service) application.RemoveChild(element);
        }

        private static System.Collections.Generic.List<XmlElement> Named(XmlElement parent, string tag)
        {
            var found = new System.Collections.Generic.List<XmlElement>();
            foreach (XmlNode node in parent.ChildNodes)
                if (node is XmlElement element && element.Name == tag) found.Add(element);
            return found;
        }
    }
}
