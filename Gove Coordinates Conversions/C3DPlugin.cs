using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using GoveCadGeodeticTransformer;

[assembly: CommandClass(typeof(GoveCivil3DPlugin.Commands))]

namespace GoveCivil3DPlugin
{
    public class Commands
    {
        /// <summary>
        /// Command to transform selected points in Civil 3D from Gove AMG to Gove MGA.
        /// </summary>
        [CommandMethod("GOVETRANSFORM")]
        public void GoveTransformCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

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
                        for (int i = 0; i < pline.NumberOfVertices; i++)
                        {
                            Point2d pt = pline.GetPoint2dAt(i);
                            var res = engine.TransformAMGToMGA(pt.X, pt.Y, elevation);
                            pline.SetPointAt(i, new Point2d(res.Easting, res.Northing));
                            
                            // Adjust elevation based on the first vertex
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
}
