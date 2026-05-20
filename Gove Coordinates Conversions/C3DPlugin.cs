using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using GoveCadGeodeticTransformer;

// Define the entry point for AutoCAD/Civil 3D to initialize the plugin
[assembly: ExtensionApplication(typeof(GoveCivil3DPlugin.PluginEntry))]
[assembly: CommandClass(typeof(GoveCivil3DPlugin.Commands))]

namespace GoveCivil3DPlugin
{
    /// <summary>
    /// Handles plugin initialization and assembly resolution for dependencies.
    /// </summary>
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            // Maintains the critical dependency link silently on assembly startup
            AppDomain.CurrentDomain.AssemblyResolve += ResolveDependencies;
        }

        public void Terminate()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveDependencies;
        }

        private Assembly? ResolveDependencies(object? sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string assemblyName = new AssemblyName(args.Name).Name ?? string.Empty;
            if (string.IsNullOrEmpty(assemblyName) || assemblyName.EndsWith(".resources")) return null;

            string assemblyPath = Path.Combine(folderPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);

            return null;
        }
    }

    public class Commands
    {
        // Cache instance for singleton modeless lifecycle
        private static GoveCivil3DPlugin.TransformerWindow? _instance;

        /// <summary>
        /// Command to launch the Gove Geodetic Transformer UI modelessly.
        /// </summary>
        [CommandMethod("GOVETRANSFORM")]
        public void GoveTransformCommand()
        {
            try
            {
                if (_instance != null && _instance.IsVisible)
                {
                    _instance.Focus();
                    return;
                }

                _instance = new GoveCivil3DPlugin.TransformerWindow();
                
                // Show modelessly to allow interaction with drawing tabs while open
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(_instance);
            }
            catch (System.Exception ex)
            {
                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[ERROR] Failed to launch UI: {ex.Message}");
            }
        }
    }
}
