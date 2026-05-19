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
            AppDomain.CurrentDomain.AssemblyResolve += ResolveDependencies;
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n[Gove Geodetic] High-Precision Engine Loaded.");
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
        /// <summary>
        /// Command to launch the Gove Geodetic Transformer UI.
        /// </summary>
        [CommandMethod("GOVETRANSFORM")]
        public void GoveTransformCommand()
        {
            try
            {
                // Launch WPF window natively via Autodesk Application Context
                var uiWindow = new GoveCivil3DPlugin.TransformerWindow();
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(uiWindow);
            }
            catch (System.Exception ex)
            {
                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[ERROR] Failed to launch UI: {ex.Message}");
            }
        }
    }
}
