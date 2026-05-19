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
            ed?.WriteMessage("\n[Gove Geodetic] Assembly Resolver initialized.");
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

            // Debug info to AutoCAD command line
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;

            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }
            else
            {
                // Only log for our specific dependencies to avoid clutter
                if (assemblyName.Contains("DotSpatial") || assemblyName.Contains("netDxf"))
                {
                    ed?.WriteMessage($"\n[Gove Geodetic] Dependency NOT FOUND: {assemblyName}");
                    ed?.WriteMessage($"\n[Gove Geodetic] Looked in: {folderPath}");
                }
            }

            return null;
        }
    }

    public class Commands
    {
        /// <summary>
        /// Command to transform selected entities in Civil 3D from Gove AMG to Gove MGA.
        /// </summary>
        [CommandMethod("GOVETRANSFORM")]
        public void GoveTransformCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using (DocumentLock loc = doc.LockDocument())
                {
                    ed.WriteMessage("\n[INFO] Initializing Gove High-Precision Geodetic Engine...");
                    var engine = new GoveGeodeticEngine();

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // Filter for Points, Lines, and Polylines
                        TypedValue[] filter = new TypedValue[] 
                        { 
                            new TypedValue((int)DxfCode.Operator, "<OR"),
                            new TypedValue((int)DxfCode.Start, "POINT"), 
                            new TypedValue((int)DxfCode.Start, "LINE"),
                            new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                            new TypedValue((int)DxfCode.Operator, "OR>")
                        };
                        
                        SelectionFilter sf = new SelectionFilter(filter);
                        PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\nSelect objects to transform: " };
                        PromptSelectionResult psr = ed.GetSelection(pso, sf);

                        if (psr.Status != PromptStatus.OK) return;

                        int transformedCount = 0;

                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so.ObjectId.IsErased) continue;

                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;

                            if (ent is DBPoint point)
                            {
                                var pos = point.Position;
                                var res = engine.TransformAMGToMGA(pos.X, pos.Y, pos.Z);
                                point.Position = new Point3d(res.Easting, res.Northing, res.Height);
                                transformedCount++;
                            }
                            else if (ent is Line line)
                            {
                                var start = line.StartPoint;
                                var end = line.EndPoint;
                                
                                var resStart = engine.TransformAMGToMGA(start.X, start.Y, start.Z);
                                var resEnd = engine.TransformAMGToMGA(end.X, end.Y, end.Z);
                                
                                line.StartPoint = new Point3d(resStart.Easting, resStart.Northing, resStart.Height);
                                line.EndPoint = new Point3d(resEnd.Easting, resEnd.Northing, resEnd.Height);
                                transformedCount++;
                            }
                            else if (ent is Polyline pline)
                            {
                                double elevation = pline.Elevation;
                                // Perform transformation and adjust elevation based on first vertex
                                for (int i = 0; i < pline.NumberOfVertices; i++)
                                {
                                    Point2d pt = pline.GetPoint2dAt(i);
                                    var res = engine.TransformAMGToMGA(pt.X, pt.Y, elevation);
                                    pline.SetPointAt(i, new Point2d(res.Easting, res.Northing));
                                    
                                    if (i == 0) pline.Elevation = res.Height;
                                }
                                transformedCount++;
                            }
                        }

                        tr.Commit();
                        ed.WriteMessage($"\n[SUCCESS] Successfully transformed {transformedCount} entities to Gove MGA.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERROR] Transformation failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    ed.WriteMessage($"\n[DETAIL] {ex.InnerException.Message}");
                }
            }
        }
    }
}
