using System;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using GoveCadGeodeticTransformer;

namespace GoveCivil3DPlugin
{
    public partial class TransformerWindow : Window
    {
        public TransformerWindow()
        {
            InitializeComponent();
        }

        private void TransformButton_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            bool isForward = DirectionComboBox.SelectedIndex == 0;

            // Hide the window to allow user drawing interaction selection safely
            this.Hide();

            try
            {
                using (DocumentLock loc = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        TypedValue[] filter = new TypedValue[] 
                        { 
                            new TypedValue((int)DxfCode.Operator, "<OR"),
                            new TypedValue((int)DxfCode.Start, "POINT"), 
                            new TypedValue((int)DxfCode.Start, "LINE"),
                            new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                            new TypedValue((int)DxfCode.Operator, "OR>")
                        };
                        
                        SelectionFilter sf = new SelectionFilter(filter);
                        PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\nSelect elements to transform: " };
                        PromptSelectionResult psr = ed.GetSelection(pso, sf);

                        if (psr.Status != PromptStatus.OK)
                        {
                            LogConsole.AppendText($"[CANCELLED] Selection aborted by operator.\n");
                            return;
                        }

                        var engine = new GoveGeodeticEngine();
                        int transformedCount = 0;

                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so.ObjectId.IsErased) continue;

                            Entity? ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;

                            if (ent is DBPoint point)
                            {
                                var pos = point.Position;
                                var res = isForward ? engine.TransformAMGToMGA(pos.X, pos.Y, pos.Z) : engine.TransformMGAToAMG(pos.X, pos.Y, pos.Z);
                                point.Position = new Point3d(res.Easting, res.Northing, res.Height);
                                transformedCount++;
                            }
                            else if (ent is Line line)
                            {
                                var start = line.StartPoint;
                                var end = line.EndPoint;
                                var resStart = isForward ? engine.TransformAMGToMGA(start.X, start.Y, start.Z) : engine.TransformMGAToAMG(start.X, start.Y, start.Z);
                                var resEnd = isForward ? engine.TransformAMGToMGA(end.X, end.Y, end.Z) : engine.TransformMGAToAMG(end.X, end.Y, end.Z);
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
                                    var res = isForward ? engine.TransformAMGToMGA(pt.X, pt.Y, elevation) : engine.TransformMGAToAMG(pt.X, pt.Y, elevation);
                                    pline.SetPointAt(i, new Point2d(res.Easting, res.Northing));
                                    if (i == 0) pline.Elevation = res.Height;
                                }
                                transformedCount++;
                            }
                        }

                        tr.Commit();
                        LogConsole.AppendText($"[SUCCESS] Transformed {transformedCount} spatial entities.\n");
                        StatusLabel.Text = $"Status: Execution Success ({transformedCount} items)";
                    }
                }
            }
            catch (Exception ex)
            {
                LogConsole.AppendText($"[CRITICAL] Error: {ex.Message}\n");
                StatusLabel.Text = "Status: Process Failed";
            }
            finally
            {
                // Bring back window smoothly upon transaction processing end
                this.ShowDialog();
            }
        }
    }
}
