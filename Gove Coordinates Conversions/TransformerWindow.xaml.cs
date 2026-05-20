using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using GoveCadGeodeticTransformer;
using Autodesk.Civil.DatabaseServices;

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
            // [MODELESS CONTEXT GUARD] Explicitly fetch MdiActiveDocument per click
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // [UI RESPONSIVENESS] Disable controls to prevent double-triggering
            ControlsStack.IsEnabled = false;

            // [TRANSACTIONAL INTEGRITY] Robust boundary safeguards
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
                        var stats = new Dictionary<string, int>();
                        bool isHorizontal = (operation == TransformType.AmgToMga || operation == TransformType.MgaToAmg);

                        UpdateLog($"[START] Initializing pipeline: {operation}...");
                        doc.TransactionManager.EnableGraphicsFlush(true);

                        foreach (ObjectId id in ms)
                        {
                            if (id.IsErased) continue;

                            Autodesk.AutoCAD.DatabaseServices.Entity? ent = tr.GetObject(id, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Entity;
                            if (ent == null) continue;

                            string typeName = ent.GetType().Name;
                            bool isModified = false;

                            // [REVISED LOGIC] Isolated Horizontal/Vertical modifications
                            if (ent is DBPoint point)
                            {
                                point.Position = ApplyTransform(point.Position, operation, engine, isHorizontal);
                                isModified = true;
                            }
                            else if (ent is Line line)
                            {
                                line.StartPoint = ApplyTransform(line.StartPoint, operation, engine, isHorizontal);
                                line.EndPoint = ApplyTransform(line.EndPoint, operation, engine, isHorizontal);
                                isModified = true;
                            }
                            else if (ent is Polyline pline)
                            {
                                for (int i = 0; i < pline.NumberOfVertices; i++)
                                {
                                    Point2d pt = pline.GetPoint2dAt(i);
                                    Point3d transformed = ApplyTransform(new Point3d(pt.X, pt.Y, 0), operation, engine, isHorizontal);
                                    pline.SetPointAt(i, new Point2d(transformed.X, transformed.Y));
                                }
                                if (!isHorizontal)
                                {
                                    Point3d elevated = ApplyTransform(new Point3d(0, 0, pline.Elevation), operation, engine, isHorizontal);
                                    pline.Elevation = elevated.Z;
                                }
                                isModified = true;
                            }
                            else if (ent is Polyline3d pline3d)
                            {
                                foreach (ObjectId vId in pline3d)
                                {
                                    if (tr.GetObject(vId, OpenMode.ForWrite) is Autodesk.AutoCAD.DatabaseServices.PolylineVertex3d v3d)
                                        v3d.Position = ApplyTransform(v3d.Position, operation, engine, isHorizontal);
                                }
                                isModified = true;
                            }
                            else if (ent is CogoPoint cogo)
                            {
                                Point3d transformed = ApplyTransform(new Point3d(cogo.Easting, cogo.Northing, cogo.Elevation), operation, engine, isHorizontal);
                                cogo.Easting = transformed.X;
                                cogo.Northing = transformed.Y;
                                if (!isHorizontal) cogo.Elevation = transformed.Z;
                                isModified = true;
                            }
                            else if (ent is BlockReference br)
                            {
                                br.Position = ApplyTransform(br.Position, operation, engine, isHorizontal);
                                isModified = true;
                            }
                            else if (ent is DBText text)
                            {
                                text.Position = ApplyTransform(text.Position, operation, engine, isHorizontal);
                                isModified = true;
                            }
                            else if (ent is MText mtext)
                            {
                                mtext.Location = ApplyTransform(mtext.Location, operation, engine, isHorizontal);
                                isModified = true;
                            }

                            if (isModified)
                            {
                                if (!stats.ContainsKey(typeName)) stats[typeName] = 0;
                                stats[typeName]++;
                                doc.TransactionManager.QueueForGraphicsFlush();

                                if (stats.Count % 50 == 0) UpdateStatus($"Processing: {stats.Count} items...");
                            }
                        }

                        tr.Commit();
                        Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
                        ed.Regen();

                        // [DYNAMIC REPORTING] Build success banner and statistics
                        string headerBanner = GetTransformationHeader(operation);
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("------------------------------------------------------------");
                        sb.AppendLine($"[SUCCESS] {headerBanner}");
                        sb.AppendLine("------------------------------------------------------------");
                        sb.AppendLine("Modified Entities Statistics:");
                        
                        int totalObjects = 0;
                        foreach (var kvp in stats)
                        {
                            sb.AppendLine($"  ➔ {kvp.Key}: {kvp.Value} changed");
                            totalObjects += kvp.Value;
                        }
                        sb.AppendLine($"Total Model Space entities updated: {totalObjects}\n");

                        UpdateLog(sb.ToString());
                        UpdateStatus($"Status: {headerBanner}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLog($"[CRITICAL ERROR] Transaction rollback: {ex.Message}");
                UpdateStatus("Status: Execution Faulted");
            }
            finally
            {
                ControlsStack.IsEnabled = true;
            }
        }

        private string GetTransformationHeader(TransformType type)
        {
            switch (type)
            {
                case TransformType.AmgToMga: return "AMG84 to MGA94 Transformation Completed";
                case TransformType.MgaToAmg: return "MGA94 to AMG84 Transformation Completed";
                case TransformType.AhdToMbhd: return "AHD09 to MBHD Elevation Shift Completed";
                case TransformType.MbhdToAhd: return "MBHD to AHD09 Elevation Shift Completed";
                default: return "Transformation Completed";
            }
        }

        private void UpdateLog(string message)
        {
            this.Dispatcher.Invoke(() => {
                LogConsole.AppendText($"{message}\n");
                LogConsole.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateStatus(string message)
        {
            this.Dispatcher.Invoke(() => {
                StatusLabel.Text = message;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private Point3d ApplyTransform(Point3d pt, TransformType type, GoveGeodeticEngine engine, bool isHorizontal)
        {
            Point3d transformed = ProcessPoint3d(pt, type, engine);
            if (isHorizontal)
                return new Point3d(transformed.X, transformed.Y, pt.Z);
            return new Point3d(pt.X, pt.Y, transformed.Z);
        }

        private Point3d ProcessPoint3d(Point3d pt, TransformType type, GoveGeodeticEngine engine)
        {
            double x = pt.X; double y = pt.Y; double z = pt.Z;
            switch (type)
            {
                case TransformType.AmgToMga:
                    var amg2mga = engine.TransformAMGToMGA(x, y, z);
                    return new Point3d(amg2mga.Easting, amg2mga.Northing, amg2mga.Height);
                case TransformType.MgaToAmg:
                    var mga2amg = engine.TransformMGAToAMG(x, y, z);
                    return new Point3d(mga2amg.Easting, mga2amg.Northing, mga2amg.Height);
                case TransformType.AhdToMbhd:
                    return new Point3d(x, y, z + MbhdShift);
                case TransformType.MbhdToAhd:
                    return new Point3d(x, y, z - MbhdShift);
                default:
                    return pt;
            }
        }
    }

    public enum TransformType { AmgToMga, MgaToAmg, AhdToMbhd, MbhdToAhd }
}
