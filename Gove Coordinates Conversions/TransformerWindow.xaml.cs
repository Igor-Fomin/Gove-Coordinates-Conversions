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
        private const double MbhdShift = 2.154; // Vertically, MBHD is 2.154m higher than AHD

        public TransformerWindow()
        {
            InitializeComponent();
        }

        private void ConvertAmgToMga_Click(object sender, RoutedEventArgs e) => ExecuteBatchTransformation(TransformType.AmgToMga);
        private void ConvertMgaToAmg_Click(object sender, RoutedEventArgs e) => ExecuteBatchTransformation(TransformType.MgaToAmg);
        private void ConvertAhdToMbhd_Click(object sender, RoutedEventArgs e) => ExecuteBatchTransformation(TransformType.AhdToMbhd);
        private void ConvertMbhdToAhd_Click(object sender, RoutedEventArgs e) => ExecuteBatchTransformation(TransformType.MbhdToAhd);

        private void ExecuteBatchTransformation(TransformType operation)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            this.Hide();

            try
            {
                using (DocumentLock loc = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable? bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        if (bt == null) return;
                        BlockTableRecord? ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                        if (ms == null) return;

                        var engine = new GoveGeodeticEngine();
                        int processedCount = 0;

                        LogConsole.AppendText($"[START] Initializing calculation pipeline: {operation}...\n");

                        foreach (ObjectId id in ms)
                        {
                            if (id.IsErased) continue;

                            Entity? ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;

                            if (ent is DBPoint point)
                            {
                                point.Position = ProcessPoint3d(point.Position, operation, engine);
                                processedCount++;
                            }
                            else if (ent is Line line)
                            {
                                line.StartPoint = ProcessPoint3d(line.StartPoint, operation, engine);
                                line.EndPoint = ProcessPoint3d(line.EndPoint, operation, engine);
                                processedCount++;
                            }
                            else if (ent is Polyline pline)
                            {
                                double currentElevation = pline.Elevation;
                                for (int i = 0; i < pline.NumberOfVertices; i++)
                                {
                                    Point2d pt = pline.GetPoint2dAt(i);
                                    Point3d transformed3d = ProcessPoint3d(new Point3d(pt.X, pt.Y, currentElevation), operation, engine);
                                    pline.SetPointAt(i, new Point2d(transformed3d.X, transformed3d.Y));
                                    if (i == 0) pline.Elevation = transformed3d.Z;
                                }
                                processedCount++;
                            }
                            else if (ent is Polyline3d pline3d)
                            {
                                foreach (ObjectId vId in pline3d)
                                {
                                    var v = tr.GetObject(vId, OpenMode.ForWrite) as Entity;
                                    if (v is Autodesk.AutoCAD.DatabaseServices.PolylineVertex3d v3d)
                                    {
                                        v3d.Position = ProcessPoint3d(v3d.Position, operation, engine);
                                    }
                                }
                                processedCount++;
                            }
                            else if (ent is Polyline2d pline2d)
                            {
                                foreach (ObjectId vId in pline2d)
                                {
                                    var v = tr.GetObject(vId, OpenMode.ForWrite) as Entity;
                                    if (v is Vertex2d v2d)
                                    {
                                        v2d.Position = ProcessPoint3d(v2d.Position, operation, engine);
                                    }
                                }
                                processedCount++;
                            }
                            else if (ent is BlockReference br)
                            {
                                br.Position = ProcessPoint3d(br.Position, operation, engine);
                                processedCount++;
                            }
                            else if (ent is DBText text)
                            {
                                text.Position = ProcessPoint3d(text.Position, operation, engine);
                                processedCount++;
                            }
                            else if (ent is MText mtext)
                            {
                                mtext.Location = ProcessPoint3d(mtext.Location, operation, engine);
                                processedCount++;
                            }
                            else if (ent is Circle circle)
                            {
                                circle.Center = ProcessPoint3d(circle.Center, operation, engine);
                                processedCount++;
                            }
                            else if (ent is Arc arc)
                            {
                                arc.Center = ProcessPoint3d(arc.Center, operation, engine);
                                processedCount++;
                            }
                            else if (ent is Ellipse ellipse)
                            {
                                ellipse.Center = ProcessPoint3d(ellipse.Center, operation, engine);
                                processedCount++;
                            }
                            else if (ent is Spline spline)
                            {
                                for (int i = 0; i < spline.NumControlPoints; i++)
                                {
                                    spline.SetControlPointAt(i, ProcessPoint3d(spline.GetControlPointAt(i), operation, engine));
                                }
                                for (int i = 0; i < spline.NumFitPoints; i++)
                                {
                                    spline.SetFitPointAt(i, ProcessPoint3d(spline.GetFitPointAt(i), operation, engine));
                                }
                                processedCount++;
                            }
                        }

                        tr.Commit();
                        LogConsole.AppendText($"[SUCCESS] Batch run completed. Processed {processedCount} Model Space items.\n");
                        StatusLabel.Text = $"Status: Success ({processedCount} items modified)";
                    }
                }
            }
            catch (Exception ex)
            {
                LogConsole.AppendText($"[CRITICAL ERROR] Pipeline execution failed: {ex.Message}\n");
                StatusLabel.Text = "Status: Execution Faulted";
            }
            finally
            {
                this.ShowDialog();
            }
        }

        private Point3d ProcessPoint3d(Point3d pt, TransformType type, GoveGeodeticEngine engine)
        {
            // Direct access for performance and precision preservation
            double x = pt.X;
            double y = pt.Y;
            double z = pt.Z;

            switch (type)
            {
                case TransformType.AmgToMga:
                    var amg2mga = engine.TransformAMGToMGA(x, y, z);
                    return new Point3d(amg2mga.Easting, amg2mga.Northing, amg2mga.Height);

                case TransformType.MgaToAmg:
                    var mga2amg = engine.TransformMGAToAMG(x, y, z);
                    return new Point3d(mga2amg.Easting, mga2amg.Northing, mga2amg.Height);

                case TransformType.AhdToMbhd:
                    // Precision-safe linear shift: retain X/Y bit-for-bit
                    return new Point3d(x, y, z + MbhdShift);

                case TransformType.MbhdToAhd:
                    // Precision-safe linear shift: retain X/Y bit-for-bit
                    return new Point3d(x, y, z - MbhdShift);

                default:
                    return pt;
            }
        }        }

    public enum TransformType
    {
        AmgToMga,
        MgaToAmg,
        AhdToMbhd,
        MbhdToAhd
    }
}