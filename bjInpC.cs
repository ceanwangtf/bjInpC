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
                
                // Default placeholder until the ' -4' line provides the real name
                currentBlockName = "UNKNOWN"; 
                
                if (line.Length >= 11)
                {
                    currentBlockName = line.Substring(6, Math.Min(6, line.Length - 6)).Trim().ToUpperInvariant();
                }
                else
                {
                    currentBlockName = line.Substring(4).Trim().ToUpperInvariant();
                }
                
                if (!mesh._nodalResults.ContainsKey(currentBlockName))
                {
                    mesh._nodalResults[currentBlockName] = new Dictionary<int, float[]>();
                }
                
                //Console.WriteLine("mode = 100");
                continue;
            }

            // Add this block directly after the 100C block to catch ' -4' and rename the dataset to 'DISP'
            if (header == "-4" || header.StartsWith("-4"))
            {
                if (line.Length >= 9)
                {
                    string realName = line.Substring(5, 4).Trim().ToUpperInvariant(); // Extracts "DISP"
                    
                    if (!string.IsNullOrEmpty(realName) && currentBlockName != realName)
                    {
                        // If the old "UNKNOWN" or "L  101" key exists, migrate it to the new name
                        if (mesh._nodalResults.ContainsKey(currentBlockName))
                        {
                            var data = mesh._nodalResults[currentBlockName];
                            mesh._nodalResults.Remove(currentBlockName);
                            mesh._nodalResults[realName] = data;
                        }
                        else if (!mesh._nodalResults.ContainsKey(realName))
                        {
                            mesh._nodalResults[realName] = new Dictionary<int, float[]>();
                        }
                        
                        currentBlockName = realName;
                        Console.WriteLine("Updated currentBlockName to: " + currentBlockName);
                    }
                }
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
                    //Console.WriteLine("-1 or -2 line");
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
                                //Console.WriteLine("v = {0}", v);
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
                        //Console.WriteLine("activeResultNodeId = {activeResultNodeId}");
                        float v;
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                            {
                                //Console.WriteLine("v = {0}", v);
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

public class MainForm : Form
{
    private bjOpenGLViewport glViewport;
    private ToolStripStatusLabel statusLabel;
    private ToolStrip toolStrip;
    private ToolStrip toolStrip1;
    private FrdMesh currentFrdMesh;
    private string loadedModelName = "Default Stepped L-Bracket";
    private bool isStlMesh = false;

    // Camera Euler angles (radians) and zoom
    //private double rotX = -0.6154797; // -35.264 deg (True Isometric pitch)
    //private double rotY = -0.7853981; // -45.0 deg (True Isometric yaw)
    private double zoom = 75.0;

    // Mouse orbit state
    private bool isDragging = false;
    private Point lastMousePos;
    private string activeView = "Iso";

    // 3D Solid Model: L-Bracket with chamfered corner (14 vertices, 9 planar faces)
    private Vec3[] vertices;
    private int[][] faces;
    private Color[] faceBaseColors;
    
    public MainForm()
    {
        this.Text = "CalculiX Inp Mesh Viewer V0.2";
        this.Width = 960;
        this.Height = 680;

        //-----------------------------------------------------------------------------------------
        // MenuStrip
        //-----------------------------------------------------------------------------------------
        MenuStrip menuStrip = new MenuStrip();

        // File Menu
        ToolStripMenuItem fileMenu = new ToolStripMenuItem("&File");
        ToolStripMenuItem openMenuItem = new ToolStripMenuItem("&Import CalculiX Mesh (.inp)...");
        openMenuItem.ShortcutKeys = Keys.Control | Keys.O;
        openMenuItem.Click += OpenMenuItem_Click;

        ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("E&xit");
        exitMenuItem.Click += (s, e) => this.Close();

        fileMenu.DropDownItems.Add(openMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exitMenuItem);

        // View Menu
        ToolStripMenuItem viewMenu = new ToolStripMenuItem("&View");
        ToolStripMenuItem toggleNodesItem = new ToolStripMenuItem("Show &Nodes");
        toggleNodesItem.CheckOnClick = true;
        toggleNodesItem.Checked = true;
        toggleNodesItem.CheckedChanged += (s, e) =>
        {
            glViewport.ShowNodes = toggleNodesItem.Checked;
            glViewport.Invalidate();
        };
        viewMenu.DropDownItems.Add(toggleNodesItem);

        menuStrip.Items.Add(fileMenu);
        menuStrip.Items.Add(viewMenu);
        this.MainMenuStrip = menuStrip;
        //-----------------------------------------------------------------------------------------
        //toolStrip
        //-----------------------------------------------------------------------------------------
        toolStrip = new ToolStrip();
        toolStrip.ImageScalingSize = new Size(24, 24);

        // Import STL button
        ToolStripButton btnOpen = new ToolStripButton("Import STL", CreateFolderIcon());
        btnOpen.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        btnOpen.TextImageRelation = TextImageRelation.ImageAboveText;
        btnOpen.Click += BtnOpenStl_Click;
        toolStrip.Items.Add(btnOpen);

        // Import Frd button
        ToolStripButton btnFrd = new ToolStripButton("Import Frd", CreateFrdFolderIcon());
        btnFrd.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        btnFrd.TextImageRelation = TextImageRelation.ImageAboveText;
        btnFrd.Click += BtnOpenFrd_Click;
        toolStrip.Items.Add(btnFrd);

        toolStrip.Items.Add(new ToolStripSeparator());

        string[] views = { "Front", "Back", "Top", "Bottom", "Left", "Right", "Iso" };

        foreach (string view in views)
        {
            if (view == "Iso")
            {
                toolStrip.Items.Add(new ToolStripSeparator());
            }

            ToolStripButton btn = new ToolStripButton(view, CreateViewIcon(view));
            btn.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            btn.TextImageRelation = TextImageRelation.ImageAboveText;
            btn.Tag = view;
            string currentView = view;
            btn.Click += (sender, e) => SetStandardView(currentView);
            toolStrip.Items.Add(btn);
        }

        toolStrip.Items.Add(new ToolStripSeparator());

        ToolStripButton btnExit = new ToolStripButton("Exit");
        btnExit.Click += (sender, e) => this.Close();
        toolStrip.Items.Add(btnExit);
        //-----------------------------------------------------------------------------------------
        //toolStrip1
        //-----------------------------------------------------------------------------------------
        // 2. Instantiate and mount the ToolStrip container structure
        toolStrip1 = new ToolStrip();
        toolStrip1.Dock = DockStyle.Top;
        this.Controls.Add(toolStrip1);
        //-----------------------------------------------------------------------------------------
        // Status Bar
        //-----------------------------------------------------------------------------------------
        StatusStrip statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("File -> Import CalculiX Mesh (.inp)... | Left-Drag: Orbit | Scroll: Zoom");
        statusStrip.Items.Add(statusLabel);
        //-----------------------------------------------------------------------------------------
        // 3. Instantiate and mount your custom OpenGL viewport render engine
        // OpenGL Viewport (ONLY instantiate once!)
        //-----------------------------------------------------------------------------------------
        glViewport = new bjOpenGLViewport
        {
            Dock = DockStyle.Fill,
            ShowNodes = true
        };

        //-----------------------------------------------------------------------------------------
        this.Controls.Add(glViewport);
        this.Controls.Add(statusStrip);
        this.Controls.Add(toolStrip);
        this.Controls.Add(menuStrip);
    }
    private void BuildDynamicToolStripUI(FrdMesh loadedMesh)
    {
        // 1. Clear out old dynamic result buttons (preserves standard file open buttons)
        // Assumes your ToolStrip control name is 'toolStrip1'
        for (int i = toolStrip.Items.Count - 1; i >= 0; i--)
        {
            // Adjust this if you want to keep permanent buttons like "Open File"
            if (toolStrip.Items[i].Tag is string)
            {
                toolStrip.Items.RemoveAt(i);
            }
        }

        // 2. Loop through every discovered result key block (e.g., DISP, STRESS, TEMP)
        foreach (string blockName in loadedMesh.NodalResults.Keys)
        {
            // 3. Create a lightweight ToolStripButton
            ToolStripButton blockButton = new ToolStripButton();
            blockButton.Text = blockName;          // Display name (e.g., "DISP")
            blockButton.Tag = blockName;           // Map data key identifier
            blockButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            
            // 4. Attach the common viewport switching click handler
            blockButton.Click += DynamicBlockButton_Click;

            // 5. Add the item to your toolbar layout line
            toolStrip.Items.Add(blockButton);
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
            glViewport.NodeDisplacements = dataset;
            glViewport.ShowDeformed = (targetBlockName == "DISP");
            glViewport.ShowNodes = true;

            // Trigger complete viewport context redraw paint executions
            glViewport.Invalidate();
        }
    }




    private void BtnOpenStl_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Title = "Import 3D STL Model";
            ofd.Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    LoadStlFile(ofd.FileName);
                    loadedModelName = Path.GetFileName(ofd.FileName);
                    isStlMesh = true;
                    SetStandardView("Iso");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load STL file:" + ex.Message, "STL Import Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
    
    private void BtnOpenFrd_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Title = "Import Frd result";
            ofd.Filter = "Frd Files (*.frd)|*.frd|All Files (*.*)|*.*";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    //LoadStlFile(ofd.FileName);
                    //loadedModelName = Path.GetFileName(ofd.FileName);
                    //isStlMesh = true;
                    
                    //var mesh = FrdMesh.Load(path);
                    //var viewport = new OpenGLViewport(mesh) { Dock = DockStyle.Fill };
                    
                    var mesh = FrdMesh.Load(ofd.FileName);
                    
                    glViewport.SetFrdMesh(mesh);

                    SetStandardView("Iso");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load Frd file:" + ex.Message, "Frd Import Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void LoadStlFile(string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        if (fileBytes.Length < 84)
            throw new InvalidDataException("File is too small to be a valid STL.");

        List<Vec3> loadedVerts = new List<Vec3>();
        List<int[]> loadedFaces = new List<int[]>();

        // Detect Binary vs ASCII STL:
        // Binary STL has 80-byte header + 4-byte uint32 triangle count + 50 bytes per triangle
        uint triCount = BitConverter.ToUInt32(fileBytes, 80);
        long expectedBinarySize = 84L + (long)triCount * 50L;

        if (expectedBinarySize == fileBytes.Length && triCount > 0)
        {
            // Parse Binary STL
            using (MemoryStream ms = new MemoryStream(fileBytes))
            using (BinaryReader br = new BinaryReader(ms))
            {
                br.ReadBytes(80); // skip header
                uint count = br.ReadUInt32();

                for (uint i = 0; i < count; i++)
                {
                    // Skip facet normal (3 floats), we compute normals from winding
                    br.ReadSingle(); br.ReadSingle(); br.ReadSingle();

                    int baseIdx = loadedVerts.Count;
                    for (int v = 0; v < 3; v++)
                    {
                        float x = br.ReadSingle();
                        float y = br.ReadSingle();
                        float z = br.ReadSingle();
                        // Map CAD Z-up to Viewer Y-up (X -> X, Z -> Y, -Y -> Z)
                        loadedVerts.Add(new Vec3(x, z, -y));
                    }
                    br.ReadUInt16(); // attribute byte count

                    loadedFaces.Add(new int[] { baseIdx, baseIdx + 1, baseIdx + 2 });
                }
            }
        }
        else
        {
            // Parse ASCII STL
            string text = Encoding.ASCII.GetString(fileBytes);
            //string[] lines = text.Split(new[] { '                ', '                ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] lines = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<Vec3> triVerts = new List<Vec3>(3);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        double x = double.Parse(parts[1], CultureInfo.InvariantCulture);
                        double y = double.Parse(parts[2], CultureInfo.InvariantCulture);
                        double z = double.Parse(parts[3], CultureInfo.InvariantCulture);
                        // Map CAD Z-up to Viewer Y-up
                        triVerts.Add(new Vec3(x, z, -y));

                        if (triVerts.Count == 3)
                        {
                            int baseIdx = loadedVerts.Count;
                            loadedVerts.Add(triVerts[0]);
                            loadedVerts.Add(triVerts[1]);
                            loadedVerts.Add(triVerts[2]);
                            loadedFaces.Add(new int[] { baseIdx, baseIdx + 1, baseIdx + 2 });
                            triVerts.Clear();
                        }
                    }
                }
            }
        }

        if (loadedFaces.Count == 0)
            throw new InvalidDataException("No triangles found in STL file.");

        // Normalize model: center at origin (0,0,0) and scale to fit unit radius ~1.8
        Vec3 min = loadedVerts[0];
        Vec3 max = loadedVerts[0];
        for (int i = 1; i < loadedVerts.Count; i++)
        {
            Vec3 p = loadedVerts[i];
            if (p.X < min.X) min.X = p.X;
            if (p.Y < min.Y) min.Y = p.Y;
            if (p.Z < min.Z) min.Z = p.Z;
            if (p.X > max.X) max.X = p.X;
            if (p.Y > max.Y) max.Y = p.Y;
            if (p.Z > max.Z) max.Z = p.Z;
        }

        Vec3 center = new Vec3((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);
        double maxExtent = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        double scale = maxExtent > 1e-9 ? (3.2 / maxExtent) : 1.0;

        vertices = new Vec3[loadedVerts.Count];
        for (int i = 0; i < loadedVerts.Count; i++)
        {
            vertices[i] = (loadedVerts[i] - center) * scale;
        }

        faces = loadedFaces.ToArray();
        faceBaseColors = new Color[faces.Length];
        Color stlSurfaceColor = Color.FromArgb(95, 175, 245);
        for (int i = 0; i < faces.Length; i++)
        {
            faceBaseColors[i] = stlSurfaceColor;
        }

        zoom = 95.0;
    }
    
    private Bitmap CreateFolderIcon()
    {
        Bitmap bmp = new Bitmap(24, 24);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            //g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            PointF[] backTab = { new PointF(2, 6), new PointF(9, 6), new PointF(11, 8), new PointF(21, 8), new PointF(21, 19), new PointF(2, 19) };
            PointF[] frontFlap = { new PointF(2, 10), new PointF(22, 10), new PointF(20, 19), new PointF(2, 19) };

            using (Brush backBrush = new SolidBrush(Color.FromArgb(215, 165, 45)))
            using (Brush frontBrush = new SolidBrush(Color.FromArgb(245, 200, 75)))
            using (Pen borderPen = new Pen(Color.FromArgb(90, 70, 20), 1.2f))
            {
                g.FillPolygon(backBrush, backTab);
                g.DrawPolygon(borderPen, backTab);
                g.FillPolygon(frontBrush, frontFlap);
                g.DrawPolygon(borderPen, frontFlap);
            }
        }
        return bmp;
    }

    private Bitmap CreateFrdFolderIcon()
    {
        Bitmap bmp = new Bitmap(24, 24);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            //g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            PointF[] backTab = { new PointF(2, 6), new PointF(9, 6), new PointF(11, 8), new PointF(21, 8), new PointF(21, 19), new PointF(2, 19) };
            PointF[] frontFlap = { new PointF(2, 10), new PointF(22, 10), new PointF(20, 19), new PointF(2, 19) };

            using (Brush backBrush = new SolidBrush(Color.FromArgb(215, 165, 45)))
            using (Brush frontBrush = new SolidBrush(Color.FromArgb(245, 200, 75)))
            using (Pen borderPen = new Pen(Color.FromArgb(90, 70, 20), 1.2f))
            {
                g.FillPolygon(backBrush, backTab);
                g.DrawPolygon(borderPen, backTab);
                g.FillPolygon(frontBrush, frontFlap);
                g.DrawPolygon(borderPen, frontFlap);
            }
        }
        return bmp;
    }
    
    private Bitmap CreateViewIcon(string view)
    {
        Bitmap bmp = new Bitmap(24, 24);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            //g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            PointF top = new PointF(12, 2);
            PointF topLeft = new PointF(3, 7);
            PointF topRight = new PointF(21, 7);
            PointF center = new PointF(12, 12);
            PointF botLeft = new PointF(3, 17);
            PointF botRight = new PointF(21, 17);
            PointF bottom = new PointF(12, 22);

            PointF[] topFace = { top, topRight, center, topLeft };
            PointF[] leftFace = { topLeft, center, bottom, botLeft };
            PointF[] rightFace = { center, topRight, botRight, bottom };

            Brush baseBrush = new SolidBrush(Color.FromArgb(235, 240, 245));
            Brush highlightBrush = new SolidBrush(Color.FromArgb(0, 120, 215));
            Pen visiblePen = new Pen(Color.FromArgb(50, 50, 50), 1.3f);
            Pen hiddenPen = new Pen(Color.FromArgb(0, 120, 215), 1.6f);
            //hiddenPen.DashStyle = DashStyle.Dash;

            g.FillPolygon(baseBrush, topFace);
            g.FillPolygon(baseBrush, leftFace);
            g.FillPolygon(baseBrush, rightFace);

            if (view == "Iso")
            {
                using (Brush isoTop = new SolidBrush(Color.FromArgb(100, 180, 245)))
                using (Brush isoLeft = new SolidBrush(Color.FromArgb(0, 120, 215)))
                using (Brush isoRight = new SolidBrush(Color.FromArgb(0, 85, 160)))
                {
                    g.FillPolygon(isoTop, topFace);
                    g.FillPolygon(isoLeft, leftFace);
                    g.FillPolygon(isoRight, rightFace);
                }
            }
            else if (view == "Top") g.FillPolygon(highlightBrush, topFace);
            else if (view == "Front") g.FillPolygon(highlightBrush, leftFace);
            else if (view == "Right") g.FillPolygon(highlightBrush, rightFace);

            g.DrawPolygon(visiblePen, topFace);
            g.DrawPolygon(visiblePen, leftFace);
            g.DrawPolygon(visiblePen, rightFace);

            if (view == "Bottom")
            {
                PointF[] bottomFace = { botLeft, bottom, botRight, center };
                g.DrawPolygon(hiddenPen, bottomFace);
            }
            else if (view == "Back")
            {
                PointF[] backRightFace = { top, topRight, botRight, center };
                g.DrawPolygon(hiddenPen, backRightFace);
            }
            else if (view == "Left")
            {
                PointF[] backLeftFace = { top, center, bottom, botLeft };
                g.DrawPolygon(hiddenPen, backLeftFace);
            }
        }
        return bmp;
    }

        
    private void OpenMenuItem_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog dlg = new OpenFileDialog())
        {
            dlg.Title = "Import CalculiX Input File";
            dlg.Filter = "CalculiX / Abaqus Input (*.inp)|*.inp|All files (*.*)|*.*";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    bjFemMesh mesh = bjFemMesh.LoadFromInp(dlg.FileName);
                    Console.WriteLine("Nodes  : {0}", mesh.Nodes.Count);
                    Console.WriteLine("Elems  : {0}", mesh.Elements.Count);
                    Console.WriteLine("Edges  : {0}", mesh.Edges.Count);
                    Console.WriteLine("Center : ({0}, {1}, {2})", mesh.CenterX, mesh.CenterY, mesh.CenterZ);
                    Console.WriteLine("Scale  : {0}", mesh.Scale);
                    
                    glViewport.SetMesh(mesh);
                    statusLabel.Text = string.Format("Loaded: {0} | Nodes: {1} | Elements: {2} | Edges: {3}",
                        Path.GetFileName(dlg.FileName), mesh.Nodes.Count, mesh.Elements.Count, mesh.Edges.Count);
                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load .inp file:\n" + ex.Message,
                        "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
    
    private void SetStandardView(string view)
    {
        activeView = view;
        switch (view)
        {
            case "Front":  glViewport.SetView(0.0,0.0); break;
            case "Back":   glViewport.SetView(0.0,Math.PI); break;
            case "Right":  glViewport.SetView(0.0,-Math.PI/2.0); break;
            case "Left":   glViewport.SetView(0.0,Math.PI/2.0); break;
            case "Top":    glViewport.SetView(Math.PI/2.0,0.0); break;
            case "Bottom": glViewport.SetView(-Math.PI,0.0); break;
            case "Iso":    glViewport.SetView(0.6154797,-Math.PI/4.0); break;
        }
        UpdateToolbarSelection();
        //viewportPanel.Invalidate();
        Invalidate();
    }
    
    
    private void UpdateToolbarSelection()
    {
        foreach (ToolStripItem item in toolStrip.Items)
        {
            ToolStripButton btn = item as ToolStripButton;
            if (btn != null && btn.Tag != null)
            {
                btn.Checked = (btn.Tag.ToString() == activeView);
            }
        }
    }


    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}


