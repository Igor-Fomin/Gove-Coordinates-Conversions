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

            // [UI RESPONSIVENESS] Disable controls and hide window to prevent interference
            ControlsStack.IsEnabled = false;
            this.Hide();

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
                        int totalObjects = 0;

                        foreach (ObjectId id in ms)
                        {
                            if (id.IsErased) continue;
                            Autodesk.AutoCAD.DatabaseServices.Entity? ent = tr.GetObject(id, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Entity;
                            if (ent == null) continue;

                            string typeName = ent.GetType().Name;
                            bool isModified = false;

                            try
                            {
                                // =========================================================
                                // TIER 1: Distributed Geometries (Point-by-Point Shifting)
                                // =========================================================
                                if (ent is DBPoint point)
                                {
                                    point.Position = ProcessPoint3d(point.Position, operation, engine);
                                    isModified = true;
                                }
                                else if (ent is Line line)
                                {
                                    line.StartPoint = ProcessPoint3d(line.StartPoint, operation, engine);
                                    line.EndPoint = ProcessPoint3d(line.EndPoint, operation, engine);
                                    isModified = true;
                                }
                                else if (ent is Polyline pline)
                                {
                                    double currentElevation = pline.Elevation;
                                    for (int i = 0; i < pline.NumberOfVertices; i++)
                                    {
                                        Point2d pt = pline.GetPoint2dAt(i);
                                        Point3d pseudo3d = new Point3d(pt.X, pt.Y, currentElevation);
                                        Point3d transformed3d = ProcessPoint3d(pseudo3d, operation, engine);
                                        pline.SetPointAt(i, new Point2d(transformed3d.X, transformed3d.Y));
                                        if (i == 0) pline.Elevation = transformed3d.Z;
                                    }
                                    isModified = true;
                                }
                                else if (ent is Polyline3d poly3d)
                                {
                                    foreach (ObjectId vxId in poly3d)
                                    {
                                        if (tr.GetObject(vxId, OpenMode.ForWrite) is PolylineVertex3d vx)
                                            vx.Position = ProcessPoint3d(vx.Position, operation, engine);
                                    }
                                    isModified = true;
                                }
                                else if (ent is Face face)
                                {
                                    face.SetVertexAt(0, ProcessPoint3d(face.GetVertexAt(0), operation, engine));
                                    face.SetVertexAt(1, ProcessPoint3d(face.GetVertexAt(1), operation, engine));
                                    face.SetVertexAt(2, ProcessPoint3d(face.GetVertexAt(2), operation, engine));
                                    face.SetVertexAt(3, ProcessPoint3d(face.GetVertexAt(3), operation, engine));
                                    isModified = true;
                                }
                                else if (typeName == "CogoPoint")
                                {
                                    dynamic cogo = (dynamic)ent;
                                    Point3d cogoLoc = new Point3d(cogo.Easting, cogo.Northing, cogo.Elevation);
                                    Point3d transformed = ProcessPoint3d(cogoLoc, operation, engine);
                                    cogo.Easting = transformed.X;
                                    cogo.Northing = transformed.Y;
                                    cogo.Elevation = transformed.Z;
                                    isModified = true;
                                }
                                // =========================================================
                                // TIER 2: UNIVERSAL FALLBACK (Matrix Rigid-Body Shifting)
                                // Catches MLeaders, Hatches, Surfaces, Text, Blocks, Regions, Solids, etc.
                                // =========================================================
                                else
                                {
                                    if (ent.Bounds.HasValue)
                                    {
                                        // 1. Find the volumetric center of the complex object
                                        Point3d min = ent.Bounds.Value.MinPoint;
                                        Point3d max = ent.Bounds.Value.MaxPoint;
                                        Point3d center = new Point3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);

                                        // 2. Calculate the geodetic shift exactly at that center point
                                        Point3d targetCenter = ProcessPoint3d(center, operation, engine);

                                        // 3. Create a 3D displacement vector
                                        Vector3d displacement = targetCenter - center;

                                        // 4. Move the entire complex object natively and safely
                                        ent.TransformBy(Matrix3d.Displacement(displacement));
                                        isModified = true;
                                    }
                                }

                                if (isModified)
                                {
                                    totalObjects++;
                                    tr.TransactionManager.QueueForGraphicsFlush();
                                }
                            }
                            catch (Autodesk.AutoCAD.Runtime.Exception)
                            {
                                // Skip locked objects silently
                                continue;
                            }
                        }

                        tr.Commit();
                        Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
                        ed.Regen();

                        string headerBanner = GetTransformationHeader(operation);
                        UpdateStatus($"Status: {headerBanner} ({totalObjects} items updated)");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Status: Execution Faulted - {ex.Message}");
            }
            finally
            {
                ControlsStack.IsEnabled = true;
                this.Show();
            }
        }

        private string GetTransformationHeader(TransformType type)
        {
            switch (type)
            {
                case TransformType.AmgToMga: return "AMG84 to MGA94 Completed";
                case TransformType.MgaToAmg: return "MGA94 to AMG84 Completed";
                case TransformType.AhdToMbhd: return "AHD09 to MBHD Shift Completed";
                case TransformType.MbhdToAhd: return "MBHD to AHD09 Shift Completed";
                default: return "Transformation Completed";
            }
        }

        private void UpdateStatus(string message)
        {
            this.Dispatcher.Invoke(() => {
                StatusLabel.Text = message;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private Point3d ProcessPoint3d(Point3d pt, TransformType type, GoveGeodeticEngine engine)
        {
            switch (type)
            {
                case TransformType.AmgToMga:
                    var mga = engine.TransformAMGToMGA(pt.X, pt.Y, pt.Z);
                    // Retain original pt.Z to prevent 3D Cartesian matrix floating-point drift
                    return new Point3d(mga.Easting, mga.Northing, pt.Z);

                case TransformType.MgaToAmg:
                    var amg = engine.TransformMGAToAMG(pt.X, pt.Y, pt.Z);
                    // Retain original pt.Z to prevent 3D Cartesian matrix floating-point drift
                    return new Point3d(amg.Easting, amg.Northing, pt.Z);

                case TransformType.AhdToMbhd:
                    // MBHD is higher: apply the explicit vertical datum scalar shift
                    return new Point3d(pt.X, pt.Y, pt.Z + MbhdShift);

                case TransformType.MbhdToAhd:
                    // Drop down to AHD standard baseline height datum
                    return new Point3d(pt.X, pt.Y, pt.Z - MbhdShift);

                default:
                    return pt;
            }
        }
    }

    public enum TransformType { AmgToMga, MgaToAmg, AhdToMbhd, MbhdToAhd }
}
