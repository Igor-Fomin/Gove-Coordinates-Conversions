using System;
using System.IO;
using System.Collections.Generic;
using netDxf;
using netDxf.Entities;
using GoveCadGeodeticTransformer;

namespace GoveCadGeodeticTransformer
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Missing parameters.");
                Console.WriteLine("Syntax: GoveCadGeodeticTransformer.exe <input_drawing.dxf> <output_drawing.dxf>");
                return;
            }

            string inputPath = args[0];
            string outputPath = args[1];

            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file path not found: {inputPath}");
                return;
            }

            try
            {
                Console.WriteLine("[INFO] Instantiating high-precision geodetic engine...");
                var engine = new GoveGeodeticEngine();

                Console.WriteLine("[INFO] Loading CAD drawing database...");
                DxfDocument dxf = DxfDocument.Load(inputPath);

                if (dxf == null)
                {
                    Console.WriteLine(" The selected file could not be parsed as a valid DXF.");
                    return;
                }

                long processedEntities = ProcessDrawingEntities(dxf, engine);

                Console.WriteLine($"[INFO] Successfully transformed {processedEntities} spatial entities.");
                Console.WriteLine("[INFO] Writing updated database to output file...");
                dxf.Save(outputPath);
                Console.WriteLine(" DXF transformation complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Transformation halted due to: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static long ProcessDrawingEntities(DxfDocument dxf, GoveGeodeticEngine engine)
        {
            long entityCount = 0;

            // Process Points
            foreach (Point point in dxf.Entities.Points)
            {
                var original = point.Position;
                var transformed = engine.TransformAMGToMGA(original.X, original.Y, original.Z);
                point.Position = new Vector3(transformed.Easting, transformed.Northing, transformed.Height);
                entityCount++;
            }

            // Process Lines
            foreach (Line line in dxf.Entities.Lines)
            {
                var start = line.StartPoint;
                var end = line.EndPoint;

                var tStart = engine.TransformAMGToMGA(start.X, start.Y, start.Z);
                var tEnd = engine.TransformAMGToMGA(end.X, end.Y, end.Z);

                line.StartPoint = new Vector3(tStart.Easting, tStart.Northing, tStart.Height);
                line.EndPoint = new Vector3(tEnd.Easting, tEnd.Northing, tEnd.Height);
                entityCount += 2;
            }

            // Process Light Weight Polylines
            foreach (Polyline2D poly in dxf.Entities.Polylines2D)
            {
                double baseElevation = poly.Elevation;
                List<Polyline2DVertex> transformedVertices = new List<Polyline2DVertex>();

                foreach (Polyline2DVertex vertex in poly.Vertexes)
                {
                    var transformed = engine.TransformAMGToMGA(vertex.Position.X, vertex.Position.Y, baseElevation);
                    var newVertex = new Polyline2DVertex(new Vector2(transformed.Easting, transformed.Northing), vertex.Bulge)
                    {
                        StartWidth = vertex.StartWidth,
                        EndWidth = vertex.EndWidth
                    };
                    transformedVertices.Add(newVertex);
                    entityCount++;
                }

                poly.Vertexes.Clear();
                foreach (var v in transformedVertices)
                {
                    poly.Vertexes.Add(v);
                }

                // Adjust global elevation plane parameter based on the first transformed node height
                if (poly.Vertexes.Count > 0)
                {
                    var elevationCheck = engine.TransformAMGToMGA(poly.Vertexes[0].Position.X, poly.Vertexes[0].Position.Y, baseElevation);
                    poly.Elevation = elevationCheck.Height;
                }
            }

            // Process Circles
            foreach (Circle circle in dxf.Entities.Circles)
            {
                var center = circle.Center;
                var transformed = engine.TransformAMGToMGA(center.X, center.Y, center.Z);
                circle.Center = new Vector3(transformed.Easting, transformed.Northing, transformed.Height);
                entityCount++;
            }

            // Process Arcs
            foreach (Arc arc in dxf.Entities.Arcs)
            {
                var center = arc.Center;
                var transformed = engine.TransformAMGToMGA(center.X, center.Y, center.Z);
                arc.Center = new Vector3(transformed.Easting, transformed.Northing, transformed.Height);
                entityCount++;
            }

            // Process single-line text elements
            foreach (Text text in dxf.Entities.Texts)
            {
                var pos = text.Position;
                var transformed = engine.TransformAMGToMGA(pos.X, pos.Y, pos.Z);
                text.Position = new Vector3(transformed.Easting, transformed.Northing, transformed.Height);
                entityCount++;
            }

            // Process multi-line text blocks
            foreach (MText mtext in dxf.Entities.MTexts)
            {
                var pos = mtext.Position;
                var transformed = engine.TransformAMGToMGA(pos.X, pos.Y, pos.Z);
                mtext.Position = new Vector3(transformed.Easting, transformed.Northing, transformed.Height);
                entityCount++;
            }

            return entityCount;
        }
    }
}
