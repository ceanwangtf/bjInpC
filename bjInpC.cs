using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

//public float rotX = 25.0f;
//public float rotY = -35.0f;

public struct Vec3
{
    public double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
    public static Vec3 operator *(Vec3 a, double s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }

    public static Vec3 Cross(Vec3 a, Vec3 b)
    {
        return new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }

    public static double Dot(Vec3 a, Vec3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    public Vec3 Normalize()
    {
        double len = Math.Sqrt(X * X + Y * Y + Z * Z);
        return len > 1e-9 ? new Vec3(X / len, Y / len, Z / len) : new Vec3(0, 0, 0);
    }
}


public class bjFemNode
{
    public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public class bjFemElement
{
    public int Id { get; set; }
    public string ElementType { get; set; }
    public List<int> NodeIds { get; set; }
    public bjFemElement()
    {
        NodeIds = new List<int>();
    }
}

public class bjFemMesh
{
    //public Dictionary<int, bjFemNode> Nodes { get; } = new Dictionary<int, bjFemNode>();
    //public List<bjFemElement> Elements { get; } = new List<bjFemElement>();
    public Dictionary<int, bjFemNode> Nodes { get; private set;}
    public List<bjFemElement> Elements { get; private set; } 

    // Normalized bounding center and scale for camera framing
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double CenterZ { get; private set; }
    public double Scale { get; private set; }
    
    
    // Deduplicated wireframe edges as pairs of node IDs
    public List<Tuple<int, int>> Edges { get; private set;  } 
    public List<int[]> SurfaceTriangles { get; private set; }

    public bjFemMesh()
    {
        Nodes = new Dictionary<int, bjFemNode>();
        Elements = new List<bjFemElement>();
        Scale = 1.0;
        Edges = new List<Tuple<int, int>>();
        SurfaceTriangles = new List<int[]>();        
    }

    public static bjFemMesh LoadFromInp(string filePath)
    {
        var mesh = new bjFemMesh();
        string currentSection = "";
        string currentElementType = "";
        bjFemElement pendingElement = null;

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("**"))
                continue;

            if (line.StartsWith("*"))
            {
                pendingElement = null;
                string upperLine = line.ToUpperInvariant();

                if (upperLine.StartsWith("*NODE"))
                {
                    currentSection = "NODE";
                }
                else if (upperLine.StartsWith("*ELEMENT"))
                {
                    currentSection = "ELEMENT";
                    currentElementType = ExtractParameter(line, "TYPE");
                }
                else
                {
                    currentSection = "";
                }
                continue;
            }

            if (currentSection == "NODE")
            {
                string[] parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int nodeId;
                if (parts.Length >= 4 && int.TryParse(parts[0].Trim(), out nodeId))
                {
                    mesh.Nodes[nodeId] = new bjFemNode
                    {
                        Id = nodeId,
                        X = ParseDouble(parts[1]),
                        Y = ParseDouble(parts[2]),
                        Z = ParseDouble(parts[3])
                    };
                }
            }
            else if (currentSection == "ELEMENT")
            {
                bool endsWithComma = line.EndsWith(",");
                string[] parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                int elemId;    
                if (pendingElement != null)
                {
                    int nid;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (int.TryParse(parts[i].Trim(), out nid))
                            pendingElement.NodeIds.Add(nid);
                    }
                    if (!endsWithComma)
                        pendingElement = null;
                }
                else if (int.TryParse(parts[0].Trim(), out elemId))
                {
                    var elem = new bjFemElement
                    {
                        Id = elemId,
                        ElementType = currentElementType
                    };

                    int nid;
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (int.TryParse(parts[i].Trim(), out nid))
                            elem.NodeIds.Add(nid);
                    }

                    mesh.Elements.Add(elem);
                    if (endsWithComma)
                        pendingElement = elem;
                }
            }
        }

        mesh.ComputeBoundsAndEdges();
        mesh.ComputeSurfaceTriangles();
        return mesh;
    }

    public void ComputeBoundsAndEdges()
    {
        if (Nodes.Count == 0)
            return;

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;

        foreach (var n in Nodes.Values)
        {
            if (n.X < minX) minX = n.X;
            if (n.X > maxX) maxX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.Y > maxY) maxY = n.Y;
            if (n.Z < minZ) minZ = n.Z;
            if (n.Z > maxZ) maxZ = n.Z;
        }

        CenterX = 0.5 * (minX + maxX);
        CenterY = 0.5 * (minY + maxY);
        CenterZ = 0.5 * (minZ + maxZ);

        double dx = maxX - minX;
        double dy = maxY - minY;
        double dz = maxZ - minZ;
        double maxExtent = Math.Max(dx, Math.Max(dy, dz));
        Scale = maxExtent > 1e-12 ? 2.0 / maxExtent : 1.0;

        // Build unique wireframe edge list based on standard CalculiX/Abaqus element topology
        var uniqueEdges = new HashSet<long>();
        foreach (var el in Elements)
        {
            string t = (el.ElementType ?? "").ToUpperInvariant();
            var n = el.NodeIds;

            if ((t.StartsWith("C3D8") || t.StartsWith("C3D20")) && n.Count >= 8)
            {
                // Hexahedron corner edges (nodes 0..7)
                int[,] hexEdges = {
                    {0,1}, {1,2}, {2,3}, {3,0},
                    {4,5}, {5,6}, {6,7}, {7,4},
                    {0,4}, {1,5}, {2,6}, {3,7}
                };
                for (int i = 0; i < 12; i++)
                    AddEdge(uniqueEdges, n[hexEdges[i, 0]], n[hexEdges[i, 1]]);
            }
            else if ((t.StartsWith("C3D4") || t.StartsWith("C3D10")) && n.Count >= 4)
            {
                // Tetrahedron corner edges (nodes 0..3)
                int[,] tetEdges = {
                    {0,1}, {1,2}, {2,0},
                    {0,3}, {1,3}, {2,3}
                };
                for (int i = 0; i < 6; i++)
                    AddEdge(uniqueEdges, n[tetEdges[i, 0]], n[tetEdges[i, 1]]);
            }
            else if ((t.StartsWith("C3D6") || t.StartsWith("C3D15")) && n.Count >= 6)
            {
                // Wedge / Prism corner edges (nodes 0..5)
                int[,] wedgeEdges = {
                    {0,1}, {1,2}, {2,0},
                    {3,4}, {4,5}, {5,3},
                    {0,3}, {1,4}, {2,5}
                };
                for (int i = 0; i < 9; i++)
                    AddEdge(uniqueEdges, n[wedgeEdges[i, 0]], n[wedgeEdges[i, 1]]);
            }
            else if ((t.StartsWith("S4") || t.StartsWith("S8") || t.StartsWith("CPS4") || t.StartsWith("CPE4")) && n.Count >= 4)
            {
                // Quad shell/2D corner edges
                AddEdge(uniqueEdges, n[0], n[1]);
                AddEdge(uniqueEdges, n[1], n[2]);
                AddEdge(uniqueEdges, n[2], n[3]);
                AddEdge(uniqueEdges, n[3], n[0]);
            }
            else if ((t.StartsWith("S3") || t.StartsWith("S6") || t.StartsWith("CPS3") || t.StartsWith("CPE3")) && n.Count >= 3)
            {
                // Triangle shell/2D corner edges
                AddEdge(uniqueEdges, n[0], n[1]);
                AddEdge(uniqueEdges, n[1], n[2]);
                AddEdge(uniqueEdges, n[2], n[0]);
            }
            else if (n.Count >= 2)
            {
                // Fallback polygon/beam loop
                for (int i = 0; i < n.Count; i++)
                    AddEdge(uniqueEdges, n[i], n[(i + 1) % n.Count]);
            }
        }
    }

    private void AddEdge(HashSet<long> set, int a, int b)
    {
        if (a == b || !Nodes.ContainsKey(a) || !Nodes.ContainsKey(b))
            return;

        int lo = Math.Min(a, b);
        int hi = Math.Max(a, b);
        long key = ((long)lo << 32) | (uint)hi;
        if (set.Add(key))
            Edges.Add(Tuple.Create(lo, hi));
    }
    public void ComputeSurfaceTriangles()
    {
        SurfaceTriangles.Clear();
        var faceCounts = new Dictionary<string, int>();
        var faceNodes = new Dictionary<string, int[]>();

        Action<int[]> addFace = (nodes) =>
        {
            int[] sorted = (int[])nodes.Clone();
            Array.Sort(sorted);
            string key = string.Join("_", sorted);

            if (faceCounts.ContainsKey(key))
                faceCounts[key]++;
            else
            {
                faceCounts[key] = 1;
                faceNodes[key] = nodes;
            }
        };

        foreach (var el in Elements)
        {
            string t = (el.ElementType ?? "").ToUpperInvariant();
            var n = el.NodeIds;

            if ((t.StartsWith("C3D8") || t.StartsWith("C3D20")) && n.Count >= 8)
            {
                addFace(new[] { n[0], n[3], n[2], n[1] });
                addFace(new[] { n[4], n[5], n[6], n[7] });
                addFace(new[] { n[0], n[1], n[5], n[4] });
                addFace(new[] { n[2], n[3], n[7], n[6] });
                addFace(new[] { n[1], n[2], n[6], n[5] });
                addFace(new[] { n[3], n[0], n[4], n[7] });
            }
            else if ((t.StartsWith("C3D4") || t.StartsWith("C3D10")) && n.Count >= 4)
            {
                addFace(new[] { n[0], n[2], n[1] });
                addFace(new[] { n[0], n[1], n[3] });
                addFace(new[] { n[1], n[2], n[3] });
                addFace(new[] { n[2], n[0], n[3] });
            }
            else if ((t.StartsWith("C3D6") || t.StartsWith("C3D15")) && n.Count >= 6)
            {
                addFace(new[] { n[0], n[2], n[1] });
                addFace(new[] { n[3], n[4], n[5] });
                addFace(new[] { n[0], n[1], n[4], n[3] });
                addFace(new[] { n[1], n[2], n[5], n[4] });
                addFace(new[] { n[2], n[0], n[3], n[5] });
            }
            else if ((t.StartsWith("S4") || t.StartsWith("S8") || t.StartsWith("CPS4") || t.StartsWith("CPE4")) && n.Count >= 4)
            {
                SurfaceTriangles.Add(new[] { n[0], n[1], n[2] });
                SurfaceTriangles.Add(new[] { n[0], n[2], n[3] });
            }
            else if ((t.StartsWith("S3") || t.StartsWith("S6") || t.StartsWith("CPS3") || t.StartsWith("CPE3")) && n.Count >= 3)
            {
                SurfaceTriangles.Add(new[] { n[0], n[1], n[2] });
            }
        }

        // Keep only exterior boundary faces (count == 1) so internal faces are culled
        foreach (var kvp in faceCounts)
        {
            if (kvp.Value == 1)
            {
                int[] f = faceNodes[kvp.Key];
                if (f.Length == 3)
                {
                    SurfaceTriangles.Add(f);
                }
                else if (f.Length == 4)
                {
                    SurfaceTriangles.Add(new[] { f[0], f[1], f[2] });
                    SurfaceTriangles.Add(new[] { f[0], f[2], f[3] });
                }
            }
        }
    }

    private static string ExtractParameter(string headerLine, string paramName)
    {
        foreach (string token in headerLine.Split(','))
        {
            string trimmed = token.Trim();
            int eqIndex = trimmed.IndexOf('=');
            if (eqIndex > 0)
            {
                string key = trimmed.Substring(0, eqIndex).Trim();
                if (string.Equals(key, paramName, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(eqIndex + 1).Trim();
            }
        }
        return "UNKNOWN";
    }

    private static double ParseDouble(string text)
    {
        string normalized = text.Trim().Replace('D', 'E').Replace('d', 'e');
        double val;
        double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        return val;
    }
}

public class FrdMesh
{
    public Dictionary<int, float[]> Nodes = new Dictionary<int, float[]>();
    public List<bjFemElement> Elements = new List<bjFemElement>();
    public List<int[]> Edges = new List<int[]>();
    public float[] Center = new float[3];
    public float Radius = 1.0f;
    // 1. Declare the private backing field and initialize it directly
    private Dictionary<string, Dictionary<int, float[]>> _nodalResults = new Dictionary<string, Dictionary<int, float[]>>();

    // 2. Expose it via a public read-only property
    public Dictionary<string, Dictionary<int, float[]>> NodalResults
    {
        get { return _nodalResults; }
    }

    public ICollection<string> GetDiscoveredResultBlocks()
    {
        return _nodalResults.Keys;
    }

    public IEnumerable<string> GetAvailableResultBlocks()
    {
        return _nodalResults.Keys;
    }

    public float[] GetNodalResult(string blockName, int nodeId)
    {
        Dictionary<int, float[]> block;
        if (_nodalResults.TryGetValue(blockName.ToUpperInvariant(), out block))
        {
            float[] res;
            if (block.TryGetValue(nodeId, out res))
                return res;
        }
        return null;
    }
    
    public static FrdMesh Load(string path)
    {
        var mesh = new FrdMesh();
        string[] lines = File.ReadAllLines(path);
        int mode = 0;
        int currentElemType = 0;
        var currentNodes = new List<int>();
        string currentBlockName = ""; // Track active nodal results block name

        int activeResultNodeId = -1;
        List<float> accumulatedValues = new List<float>();

        float[] min = { float.MaxValue, float.MaxValue, float.MaxValue };
        float[] max = { float.MinValue, float.MinValue, float.MinValue };

        foreach (string line in lines)
        {
            if (line.Length < 4) continue;
            string header = line.Substring(0, Math.Min(6, line.Length)).Trim();

            // 1. Intercept state markers and mode switch definitions
            if (header == "2C" || header.StartsWith("2C")) { mode = 2; continue; }
            if (header == "3C" || header.StartsWith("3C")) { mode = 3; continue; }
            if (header == "100C" || header.StartsWith("100C"))
            {
                mode = 100;
                // Parse dataset name (typically positions 7-12 in standard CalculiX format, or trailing chars)
                currentBlockName = "UNKNOWN";
                if (line.Length >= 11)
                {
                    currentBlockName = line.Substring(6, Math.Min(6, line.Length - 6)).Trim().ToUpperInvariant();
                }
                else
                {
                    currentBlockName = line.Substring(4).Trim().ToUpperInvariant();
                }
                
                // Initialize the sub-dictionary under mesh._nodalResults for this block name
                if (!mesh._nodalResults.ContainsKey(currentBlockName))
                {
                    mesh._nodalResults[currentBlockName] = new Dictionary<int, float[]>();
                }
                
                Console.WriteLine("mode = 100");
                Console.WriteLine("currentBlockName = "+currentBlockName);

                continue;
            }

            if (header == "-3")
            {
                if (mode == 3)
                {
                    FlushElement(mesh, currentElemType, currentNodes);
                    currentNodes.Clear();
                }
                mode = 0;
                continue;
            }

            // 2. Process active block mode payloads
            if (mode == 2 && header == "-1")
            {
                // Separate glued minus signs (e.g. "1.23E+00-4.56E+00") while preserving "E-"
                string payload = line.Substring(3)
                    .Replace("E-", "E_").Replace("e-", "e_")
                    .Replace("-", " -")
                    .Replace("E_", "E-").Replace("e_", "e-");

                string[] parts = payload.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                int id;
                float x, y, z;
                if (parts.Length >= 4 &&
                    int.TryParse(parts[0], out id) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    mesh.Nodes[id] = new[] { x, y, z };
                    if (x < min[0]) min[0] = x; if (x > max[0]) max[0] = x;
                    if (y < min[1]) min[1] = y; if (y > max[1]) max[1] = y;
                    if (z < min[2]) min[2] = z; if (z > max[2]) max[2] = z;
                }
            }
            else if (mode == 3)
            {
                if (header == "-1")
                {
                    FlushElement(mesh, currentElemType, currentNodes);
                    currentNodes.Clear();

                    // Format: -1 <elemId> <elemType> <group> <material>
                    string[] parts = line.Substring(3).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        int.TryParse(parts[1], out currentElemType);
                    }
                }
                else if (header == "-2")
                {
                    // Format: -2 <n1> <n2> ...
                    string[] parts = line.Substring(3).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string tok in parts)
                    {
                        int nid;
                        if (int.TryParse(tok, out nid))
                            currentNodes.Add(nid);
                    }
                }
            }
            else if (mode == 100)
            {
                // Clean up glued minus signs in result floating point notations
                string payload = line
                    .Replace("E-", "E_").Replace("e-", "e_")
                    .Replace("-", " -")
                    .Replace("E_", "E-").Replace("e_", "e-");

                string[] parts = payload.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                // CalculiX uses "-1" or "-2" to signify a data line under block 100
                if (parts[0] == "-1" || parts[0] == "-2")
                {
                    Console.WriteLine("-1 or -2 line");
                    // Flush previous node results buffer if one was active
                    if (activeResultNodeId != -1 && accumulatedValues.Count > 0)
                    {
                        mesh._nodalResults[currentBlockName][activeResultNodeId] = accumulatedValues.ToArray();
                    }

                    // Initialize state for the new node block
                    accumulatedValues.Clear();
                    
                    if (parts.Length >= 2 && int.TryParse(parts[1], out activeResultNodeId))
                    {
                        float v;
                        for (int i = 2; i < parts.Length; i++)
                        {
                            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                            {
                                Console.WriteLine("v = {0}", v);
                                accumulatedValues.Add(v);
                            }
                        }
                    }
                }
                else
                {
                    // This is a wrapped continuation line! Append float values to the current active node
                    if (activeResultNodeId != -1)
                    {
                        Console.WriteLine("activeResultNodeId = {activeResultNodeId}");
                        float v;
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                            {
                                Console.WriteLine("v = {0}", v);
                                accumulatedValues.Add(v);
                            }
                        }
                    }
                }
            }

        }

        if (mesh.Nodes.Count > 0)
        {
            for (int i = 0; i < 3; i++)
                mesh.Center[i] = (min[i] + max[i]) * 0.5f;

            float dx = max[0] - min[0];
            float dy = max[1] - min[1];
            float dz = max[2] - min[2];
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            mesh.Radius = Math.Max(len * 0.5f, 0.001f);
        }
        return mesh;
    }
        
    private static void FlushElement(FrdMesh mesh, int elemType, List<int> n)
    {
        if (n.Count == 0) return;

        if (n.Count > 0)
        {
            var el = new bjFemElement { ElementType = elemType.ToString() };
            el.NodeIds.AddRange(n);
            mesh.Elements.Add(el);
        }

        if ((elemType == 1 || elemType == 4) && n.Count >= 8)
        {
            int[,] hexEdges = { {0,1},{1,2},{2,3},{3,0}, {4,5},{5,6},{6,7},{7,4}, {0,4},{1,5},{2,6},{3,7} };
            for (int i = 0; i < 12; i++)
                mesh.Edges.Add(new[] { n[hexEdges[i, 0]], n[hexEdges[i, 1]] });
        }
        else if ((elemType == 3 || elemType == 6) && n.Count >= 4)
        {
            int[,] tetEdges = { {0,1},{1,2},{2,0}, {0,3},{1,3},{2,3} };
            for (int i = 0; i < 6; i++)
                mesh.Edges.Add(new[] { n[tetEdges[i, 0]], n[tetEdges[i, 1]] });
        }
        else
        {
            for (int i = 0; i < n.Count; i++)
                mesh.Edges.Add(new[] { n[i], n[(i + 1) % n.Count] });
        }
    }
}


public class bjOpenGLViewport : Control
{
    // Native Windows OpenGL P/Invoke declarations (zero external DLL dependencies)
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr hdc, [In] ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, [In] ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll")]
    private static extern bool SwapBuffers(IntPtr hdc);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern void glViewport(int x, int y, int width, int height);

    [DllImport("opengl32.dll")]
    private static extern void glClearColor(float red, float green, float blue, float alpha);

    [DllImport("opengl32.dll")]
    private static extern void glClear(uint mask);

    [DllImport("opengl32.dll")]
    private static extern void glEnable(uint cap);

    [DllImport("opengl32.dll")]
    private static extern void glMatrixMode(uint mode);

    [DllImport("opengl32.dll")]
    private static extern void glLoadIdentity();

    [DllImport("opengl32.dll")]
    private static extern void glFrustum(double left, double right, double bottom, double top, double zNear, double zFar);

    [DllImport("opengl32.dll")]
    private static extern void glTranslated(double x, double y, double z);

    [DllImport("opengl32.dll")]
    private static extern void glRotated(double angle, double x, double y, double z);

    [DllImport("opengl32.dll")]
    private static extern void glBegin(uint mode);

    [DllImport("opengl32.dll")]
    private static extern void glEnd();

    [DllImport("opengl32.dll")]
    private static extern void glColor3f(float red, float green, float blue);

    [DllImport("opengl32.dll")]
    private static extern void glVertex3d(double x, double y, double z);

    [DllImport("opengl32.dll")]
    private static extern void glLineWidth(float width);

    [DllImport("opengl32.dll")]
    private static extern void glPointSize(float size);
    
    [DllImport("opengl32.dll")]
    private static extern void glDisable(uint cap);

    [DllImport("opengl32.dll")]
    private static extern void glPolygonOffset(float factor, float units);

    [DllImport("opengl32.dll")]
    private static extern void glNormal3d(double nx, double ny, double nz);

    private const uint GL_TRIANGLES = 0x0004;
    private const uint GL_POLYGON_OFFSET_FILL = 0x8037;

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const byte PFD_TYPE_RGBA = 0;
    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
    private const uint GL_DEPTH_TEST = 0x0B71;
    private const uint GL_MODELVIEW = 0x1700;
    private const uint GL_PROJECTION = 0x1701;
    private const uint GL_LINES = 0x0001;
    private const uint GL_POINTS = 0x0000;

    private IntPtr hdc = IntPtr.Zero;
    private IntPtr hglrc = IntPtr.Zero;

    private bjFemMesh currentMesh;
    private FrdMesh frdMesh;
    private double rotX = -0.6154797; // -35.264 deg (True Isometric pitch)
    private double rotY = -0.7853981; // -45.0 deg (True Isometric yaw)
    private double zoomDistance = 3.5;
    private Point lastMousePos;
    private bool isDragging = false;
    // Add these lines inside your bjOpenGLViewport class
    public Dictionary<int, float[]> NodeDisplacements { get; set; }
    public float DisplacementScaleFactor { get; set; }
    public bool ShowDeformed { get; set; }

    public bool ShowNodes { get; set; } 
    
    private void ApplyColorMap(double value)
    {
        // Clamp the incoming value strictly between 0 and 1
        double v = Math.Max(0.0, Math.Min(1.0, value));
        
        float r = 0.0f, g = 0.0f, b = 0.0f;

        // Jet / Rainbow color ramp mapping logic
        if (v < 0.25) {
            r = 0.0f;
            g = (float)(4.0 * v);
            b = 1.0f;
        } else if (v < 0.5) {
            r = 0.0f;
            g = 1.0f;
            b = (float)(1.0 - 4.0 * (v - 0.25));
        } else if (v < 0.75) {
            r = (float)(4.0 * (v - 0.5));
            g = 1.0f;
            b = 0.0f;
        } else {
            r = 1.0f;
            g = (float)(1.0 - 4.0 * (v - 0.75));
            b = 0.0f;
        }

        glColor3f(r, g, b);
    }

    public bjOpenGLViewport()
    {
        this.SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        // Add your legacy-compatible property initializations here:
        this.DisplacementScaleFactor = 1.0f;
        this.ShowDeformed = false;
        this.NodeDisplacements = new Dictionary<int, float[]>();
    }

    public void SetView(double newRotX, double newRotY)
    {
        this.rotX = newRotX;
        this.rotY = newRotY;
        this.Invalidate();
    }

    public void SetMesh(bjFemMesh mesh)
    {
        currentMesh = mesh;
        //rotX = 25.0f;
        //rotY = -35.0f;
        zoomDistance = 3.5;
        Invalidate();
    }
    
    
    public void SetFrdMesh(FrdMesh frd)
    {
        if (frd == null) return;
        bjFemMesh fem = new bjFemMesh();

        // 1. Copy Nodes geometry
        foreach (var kvp in frd.Nodes)
        {
            float[] n = kvp.Value;
            if (n != null && n.Length >= 3)
            {
                fem.Nodes[kvp.Key] = new bjFemNode
                {
                    Id = kvp.Key,
                    X = n[0],
                    Y = n[1],
                    Z = n[2]
                };
            }
        }

        // 2. Copy precomputed element wireframe edges
        foreach (int[] edge in frd.Edges)
        {
            if (edge != null && edge.Length >= 2)
            {
                fem.Edges.Add(Tuple.Create(edge[0], edge[1]));
            }
        }

        // 3. Copy elements and map type tags
        foreach (var el in frd.Elements)
        {
            int typeCode;
            int.TryParse(el.ElementType, out typeCode);
            var femEl = new bjFemElement
            {
                Id = el.Id,
                ElementType = MapFrdTypeToInpType(typeCode)
            };
            femEl.NodeIds.AddRange(el.NodeIds);
            fem.Elements.Add(femEl);
        }

        // 4. Extract and assign the newly parsed Nodal Displacements payload
        // Checks standard CalculiX header permutations ('DISP' block name)
        Dictionary<int, float[]> dispBlock;
        if (frd.NodalResults.TryGetValue("DISP", out dispBlock))
        {
            this.NodeDisplacements = dispBlock;
            this.ShowDeformed = true;
            this.DisplacementScaleFactor = 20.0f; // Scale up default to amplify microscopic strains visibly
        }
        else
        {
            this.NodeDisplacements = new Dictionary<int, float[]>();
            this.ShowDeformed = false;
            this.DisplacementScaleFactor = 1.0f;
        }

        // 5. Compute geometry properties and layout boundaries
        fem.ComputeBoundsAndEdges();
        fem.ComputeSurfaceTriangles();
        this.currentMesh = fem;

        // 6. Recalculate color spectrum limits for shading loops
        this.ComputeResultBounds();

        // 7. Force UI graphics repaint execution
        this.Invalidate();
    }

    private string MapFrdTypeToInpType(int frdType)
    {
    
        // Standard CalculiX .frd element type codes
        switch (frdType)
        {
            case 1: return "C3D8";
            case 2: return "C3D6";
            case 3: return "C3D4";
            case 4: return "C3D20";
            case 5: return "C3D15";
            case 6: return "C3D10";
            case 7: return "S3";
            case 8: return "S6";
            case 9: return "S4";
            case 10: return "S8";
            default: return "";
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hdc = GetDC(this.Handle);

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR)),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = PFD_TYPE_RGBA,
            cColorBits = 24,
            cDepthBits = 24
        };

        int pixelFormat = ChoosePixelFormat(hdc, ref pfd);
        SetPixelFormat(hdc, pixelFormat, ref pfd);

        hglrc = wglCreateContext(hdc);
        wglMakeCurrent(hdc, hglrc);
        glEnable(GL_DEPTH_TEST);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hglrc != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(hglrc);
            hglrc = IntPtr.Zero;
        }
        if (hdc != IntPtr.Zero)
        {
            ReleaseDC(this.Handle, hdc);
            hdc = IntPtr.Zero;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            isDragging = true;
            lastMousePos = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (isDragging)
        {
            rotY += (e.X - lastMousePos.X) * 0.5f;
            rotX += (e.Y - lastMousePos.Y) * 0.5f;
            lastMousePos = e.Location;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        isDragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        zoomDistance -= e.Delta * 0.002;
        if (zoomDistance < 0.3) zoomDistance = 0.3;
        if (zoomDistance > 25.0) zoomDistance = 25.0;
        Invalidate();
    }
    
    // Local state variables to track within the viewport class or method
    private double minResultVal = 0.0;
    private double maxResultVal = 1.0;

    private void ComputeResultBounds()
    {
        if (NodeDisplacements == null || NodeDisplacements.Count == 0) return;

        double minVal = double.MaxValue;
        double maxVal = double.MinValue;

        foreach (var kvp in NodeDisplacements)
        {
            float[] vec = kvp.Value;
            if (vec == null || vec.Length < 3) continue;

            // Calculate 3D magnitude (hypotenuse)
            double mag = Math.Sqrt(vec[0] * vec[0] + vec[1] * vec[1] + vec[2] * vec[2]);

            if (mag < minVal) minVal = mag;
            if (mag > maxVal) maxVal = mag;
        }

        // Prevent divide-by-zero on uniform results distributions
        minResultVal = minVal;
        maxResultVal = (maxVal - minVal > 1e-9) ? maxVal : minVal + 1.0;
    }


    protected override void OnPaint(PaintEventArgs e)
    {
    
        base.OnPaint(e);
        if (hglrc == IntPtr.Zero || hdc == IntPtr.Zero)
            return;

        wglMakeCurrent(hdc, hglrc);

        int w = Math.Max(1, this.ClientSize.Width);
        int h = Math.Max(1, this.ClientSize.Height);
        glViewport(0, 0, w, h);

        glClearColor(0.11f, 0.13f, 0.16f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        // Set up perspective projection
        glMatrixMode(GL_PROJECTION);
        glLoadIdentity();
        double aspect = (double)w / h;
        double zNear = 0.1;
        double zFar = 100.0;
        double fovRad = 45.0 * Math.PI / 180.0;
        double top = zNear * Math.Tan(fovRad * 0.5);
        double right = top * aspect;
        glFrustum(-right, right, -top, top, zNear, zFar);

        // Set up camera transform
        glMatrixMode(GL_MODELVIEW);
        glLoadIdentity();
        glTranslated(0.0, 0.0, -zoomDistance);
        glRotated(rotX, 1.0, 0.0, 0.0);
        glRotated(rotY, 0.0, 1.0, 0.0);

        DrawCoordinateAxes();

        if (currentMesh != null && currentMesh.Nodes.Count > 0)
        {
        
            //Console.WriteLine("Drawing {0} edges, Scale={1}, Zoom={2}", currentMesh.Edges.Count, currentMesh.Scale, zoomDistance);

            double cx = currentMesh.CenterX;
            double cy = currentMesh.CenterY;
            double cz = currentMesh.CenterZ;
            double s = currentMesh.Scale;

            // Draw element edges
            glLineWidth(1.2f);
            glColor3f(0.25f, 0.85f, 0.95f);
            glBegin(GL_LINES);
            foreach (var edge in currentMesh.Edges)
            {
                bjFemNode n1 = currentMesh.Nodes[edge.Item1];
                bjFemNode n2 = currentMesh.Nodes[edge.Item2];
                //Console.WriteLine("Edge {0}->{1}: ({2},{3},{4}) to ({5},{6},{7})",
                //    n1.Id, n2.Id, n1.X, n1.Y, n1.Z, n2.X, n2.Y, n2.Z);
                //Console.WriteLine("Transformed v1: ({0}, {1}, {2})",
                //    (n1.X - cx) * s, (n1.Y - cy) * s, (n1.Z - cz) * s);
                
                glVertex3d((n1.X - cx) * s, (n1.Y - cy) * s, (n1.Z - cz) * s);
                glVertex3d((n2.X - cx) * s, (n2.Y - cy) * s, (n2.Z - cz) * s);
            }
            glEnd();
            //Console.WriteLine("Finished drawing edges");

            // Optionally draw node vertices
            if (ShowNodes || currentMesh.Edges.Count == 0)
            {
                glPointSize(4.0f);
                glColor3f(1.0f, 0.75f, 0.2f);
                glBegin(GL_POINTS);
                foreach (var n in currentMesh.Nodes.Values)
                {
                    glVertex3d((n.X - cx) * s, (n.Y - cy) * s, (n.Z - cz) * s);
                }
                glEnd();
            }
        
            // Draw Shaded 3D Exterior Faces
            if (currentMesh.SurfaceTriangles != null && currentMesh.SurfaceTriangles.Count > 0)
            {
                glEnable(GL_POLYGON_OFFSET_FILL);
                glPolygonOffset(1.0f, 1.0f);

                // Compute camera view direction from rotX/rotY for smooth headlight shading
                double rx = rotX * Math.PI / 180.0;
                double ry = rotY * Math.PI / 180.0;
                double lx = Math.Sin(ry) * Math.Cos(rx);
                double ly = -Math.Sin(rx);
                double lz = Math.Cos(ry) * Math.Cos(rx);

                glBegin(GL_TRIANGLES);
                foreach (int[] tri in currentMesh.SurfaceTriangles)
                {
                    bjFemNode n0, n1, n2;
                    if (!currentMesh.Nodes.TryGetValue(tri[0], out n0) ||
                        !currentMesh.Nodes.TryGetValue(tri[1], out n1) ||
                        !currentMesh.Nodes.TryGetValue(tri[2], out n2))
                        continue;

                    double x0 = (n0.X - currentMesh.CenterX) * currentMesh.Scale;
                    double y0 = (n0.Y - currentMesh.CenterY) * currentMesh.Scale;
                    double z0 = (n0.Z - currentMesh.CenterZ) * currentMesh.Scale;

                    double x1 = (n1.X - currentMesh.CenterX) * currentMesh.Scale;
                    double y1 = (n1.Y - currentMesh.CenterY) * currentMesh.Scale;
                    double z1 = (n1.Z - currentMesh.CenterZ) * currentMesh.Scale;

                    double x2 = (n2.X - currentMesh.CenterX) * currentMesh.Scale;
                    double y2 = (n2.Y - currentMesh.CenterY) * currentMesh.Scale;
                    double z2 = (n2.Z - currentMesh.CenterZ) * currentMesh.Scale;

                    // Cross product for face normal
                    double ux = x1 - x0, uy = y1 - y0, uz = z1 - z0;
                    double vx = x2 - x0, vy = y2 - y0, vz = z2 - z0;
                    double nx = uy * vz - uz * vy;
                    double ny = uz * vx - ux * vz;
                    double nz = ux * vy - uy * vx;
                    double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-12) { nx /= len; ny /= len; nz /= len; }

                    // Headlight diffuse shading (works with zero GL_LIGHTING state setup)
                    double dot = Math.Abs(nx * lx + ny * ly + nz * lz);
                    float shade = (float)(0.30 + 0.70 * dot);

                    //glColor3f(0.28f * shade, 0.58f * shade, 0.88f * shade);
                    //glNormal3d(nx, ny, nz);
                    //glVertex3d(x0, y0, z0);
                    //glVertex3d(x1, y1, z1);
                    //glVertex3d(x2, y2, z2);
                    // Vertex 0 Color and Position
                    double mag0 = GetNodeMagnitude(n0.Id);
                    ApplyColorMap((mag0 - minResultVal) / (maxResultVal - minResultVal));
                    glVertex3d(x0, y0, z0);

                    // Vertex 1 Color and Position
                    double mag1 = GetNodeMagnitude(n1.Id);
                    ApplyColorMap((mag1 - minResultVal) / (maxResultVal - minResultVal));
                    glVertex3d(x1, y1, z1);

                    // Vertex 2 Color and Position
                    double mag2 = GetNodeMagnitude(n2.Id);
                    ApplyColorMap((mag2 - minResultVal) / (maxResultVal - minResultVal));
                    glVertex3d(x2, y2, z2);                    
                }
                glEnd();

                glDisable(GL_POLYGON_OFFSET_FILL);
            }
        }

        SwapBuffers(hdc);
    }
    private double GetNodeMagnitude(int nodeId)
    {
        float[] vec; // Declared explicitly for backwards compatibility 
        if (NodeDisplacements != null && NodeDisplacements.TryGetValue(nodeId, out vec) && vec.Length >= 3)
        {
            return Math.Sqrt(vec[0] * vec[0] + vec[1] * vec[1] + vec[2] * vec[2]);
        }
        return 0.0;
    }

    private void DrawCoordinateAxes()
    {
        glLineWidth(2.0f);
        glBegin(GL_LINES);
        // X axis (Red)
        glColor3f(0.9f, 0.25f, 0.25f);
        glVertex3d(0, 0, 0);
        glVertex3d(0.35, 0, 0);
        // Y axis (Green)
        glColor3f(0.25f, 0.9f, 0.25f);
        glVertex3d(0, 0, 0);
        glVertex3d(0, 0.35, 0);
        // Z axis (Blue)
        glColor3f(0.3f, 0.55f, 1.0f);
        glVertex3d(0, 0, 0);
        glVertex3d(0, 0, 0.35);
        glEnd();
    }
    
    
}
// ====================================================================
// PLACE THIS AT THE VERY BOTTOM OF YOUR bjInpC.cs FILE
// ====================================================================
// ====================================================================
// PLACE THIS EXCLUSIVELY AT THE VERY BOTTOM OF YOUR bjInpC.cs FILE
// ====================================================================

public class MainForm : Form
{
    private ToolStrip toolStrip1;
    private bjOpenGLViewport myViewport;
    private FrdMesh currentFrdMesh;

    public MainForm()
    {
        // 1. Configure Form layout sizes
        this.Text = "CalculiX FEM Result Viewer Engine";
        this.Size = new Size(1024, 768);

        //-----------------------------------------------------------------------------------------
        //toolStrip1
        //-----------------------------------------------------------------------------------------
        // 2. Instantiate and mount the ToolStrip container structure
        toolStrip1 = new ToolStrip();
        toolStrip1.Dock = DockStyle.Top;
        this.Controls.Add(toolStrip1);

        // 3. Instantiate and mount your custom OpenGL viewport render engine
        myViewport = new bjOpenGLViewport();
        myViewport.Dock = DockStyle.Fill;
        this.Controls.Add(myViewport);

        // ====================================================================
        // AUTOMATIC TEST RUNNER PIPELINE
        // ====================================================================
        string targetFrdFile = "ccxC3D10BoxAnalysisStatic.frd"; // Change this to your target test file name

        if (File.Exists(targetFrdFile))
        {
            try
            {
                // A. Run file reader matrix loop processing arrays
                this.currentFrdMesh = FrdMesh.Load(targetFrdFile);

                // B. Map geometry and node coordinates to OpenGL context properties
                this.myViewport.SetFrdMesh(this.currentFrdMesh);

                // C. Reconstruct tool bar button toggles dynamically
                this.BuildDynamicToolStripUI(this.currentFrdMesh);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Pipeline execution pass failed: " + ex.Message);
            }
        }
    }

    private void BuildDynamicToolStripUI(FrdMesh loadedMesh)
    {
        if (loadedMesh == null || loadedMesh.NodalResults == null) return;

        // Clean out historical items from the toolbar strip row mapping
        for (int i = toolStrip1.Items.Count - 1; i >= 0; i--)
        {
            if (toolStrip1.Items[i].Tag is string)
            {
                toolStrip1.Items.RemoveAt(i);
            }
        }

        // Loop through the result block keys (e.g. DISP, STRESS) and generate buttons
        foreach (string blockName in loadedMesh.NodalResults.Keys)
        {
            ToolStripButton blockButton = new ToolStripButton();
            blockButton.Text = blockName;
            blockButton.Tag = blockName; // Keep track of block token
            blockButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            blockButton.Click += new EventHandler(DynamicBlockButton_Click);

            toolStrip1.Items.Add(blockButton);
        }
    }

    private void DynamicBlockButton_Click(object sender, EventArgs e)
    {
        ToolStripButton clickedButton = sender as ToolStripButton;
        if (clickedButton == null) return;

        string targetBlockName = clickedButton.Tag as string;
        if (targetBlockName == null || currentFrdMesh == null || currentFrdMesh.NodalResults == null) return;

        // Explicit lookup pattern safely compatible with older syntax standards
        if (currentFrdMesh.NodalResults.ContainsKey(targetBlockName))
        {
            Dictionary<int, float[]> dataset = currentFrdMesh.NodalResults[targetBlockName];
            
            // Assign active data vectors over to viewport canvas properties
            myViewport.NodeDisplacements = dataset;
            myViewport.ShowDeformed = (targetBlockName == "DISP");
            myViewport.ShowNodes = true;

            // Trigger complete viewport context redraw paint executions
            myViewport.Invalidate();
        }
    }

    // ====================================================================
    // RUNNER ENTRY POINT
    // ====================================================================
    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
