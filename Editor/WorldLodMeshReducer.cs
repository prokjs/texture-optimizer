using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prokjs.TextureOptimizer
{
// Editor-only endpoint edge collapse. Never removes arbitrary faces or fills openings.
public static class WorldLodMeshReducer
{
    private sealed class Face
    {
        public int a, b, c, sub;
        public bool alive = true;
        public IEnumerable<int> Vertices { get { yield return a; yield return b; yield return c; } }
        public bool Has(int v) { return a == v || b == v || c == v; }
        public int Other(int x, int y) { return a != x && a != y ? a : b != x && b != y ? b : c; }
        public void Replace(int from, int to) { if (a == from) a = to; if (b == from) b = to; if (c == from) c = to; }
    }
    private struct Edge : IEquatable<Edge>
    {
        public int a, b;
        public Edge(int x, int y) { a = Math.Min(x, y); b = Math.Max(x, y); }
        public bool Equals(Edge e) { return a == e.a && b == e.b; }
        public override bool Equals(object o) { return o is Edge && Equals((Edge)o); }
        public override int GetHashCode() { return unchecked(a * 397 ^ b); }
    }
    private struct Candidate
    {
        public int from, to, vf, vt;
        public float cost;
    }
    private sealed class Heap
    {
        private readonly List<Candidate> items = new List<Candidate>();
        public int Count { get { return items.Count; } }
        public void Add(Candidate c)
        {
            int i = items.Count; items.Add(c);
            while (i > 0) { int p = (i - 1) / 2; if (items[p].cost <= c.cost) break; items[i] = items[p]; i = p; }
            items[i] = c;
        }
        public Candidate Pop()
        {
            var result = items[0]; var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1);
            if (items.Count == 0) return result;
            int i = 0;
            while (i * 2 + 1 < items.Count)
            {
                int child = i * 2 + 1;
                if (child + 1 < items.Count && items[child + 1].cost < items[child].cost) child++;
                if (last.cost <= items[child].cost) break;
                items[i] = items[child]; i = child;
            }
            items[i] = last; return result;
        }
    }
    private sealed class Attributes
    {
        public Vector3[] positions, normals;
        public Vector4[] tangents;
        public Color[] colors;
        public readonly List<Vector4>[] uv = new List<Vector4>[8];
        public Attributes(Mesh mesh)
        {
            positions = mesh.vertices; normals = mesh.normals; tangents = mesh.tangents; colors = mesh.colors;
            for (int i = 0; i < 8; i++) { uv[i] = new List<Vector4>(); mesh.GetUVs(i, uv[i]); }
        }
        public bool Same(int a, int b)
        {
            if (!positions[a].Equals(positions[b])) return false;
            if (normals.Length == positions.Length && !normals[a].Equals(normals[b])) return false;
            if (tangents.Length == positions.Length && !tangents[a].Equals(tangents[b])) return false;
            if (colors.Length == positions.Length && !colors[a].Equals(colors[b])) return false;
            for (int i = 0; i < 8; i++) if (uv[i].Count == positions.Length && !uv[i][a].Equals(uv[i][b])) return false;
            return true;
        }
        public int Hash(int v)
        {
            int h = positions[v].GetHashCode();
            unchecked
            {
                if (normals.Length == positions.Length) h = h * 397 ^ normals[v].GetHashCode();
                if (tangents.Length == positions.Length) h = h * 397 ^ tangents[v].GetHashCode();
                if (colors.Length == positions.Length) h = h * 397 ^ colors[v].GetHashCode();
                for (int i = 0; i < 8; i++) if (uv[i].Count == positions.Length) h = h * 397 ^ uv[i][v].GetHashCode();
            }
            return h;
        }
    }
    private sealed class Reduction
    {
        private readonly Mesh source;
        private readonly Attributes data;
        private readonly List<Face> faces = new List<Face>();
        private readonly HashSet<int>[] incident;
        private readonly bool[] locked, alive, boundary;
        private readonly int[] version;
        private readonly float[] radius;
        private readonly Heap heap = new Heap();
        private readonly float maxDisplacement;
        private readonly bool surfaceError;
        private int triangleCount;
        public Reduction(Mesh mesh, float ratio, float relativeDisplacement = -1f)
        {
            source = mesh; data = new Attributes(mesh); int n = data.positions.Length;
            incident = new HashSet<int>[n]; locked = new bool[n]; alive = new bool[n]; boundary = new bool[n]; version = new int[n]; radius = new float[n];
            for (int i = 0; i < n; i++) { incident[i] = new HashSet<int>(); alive[i] = true; }
            maxDisplacement = mesh.bounds.size.magnitude * (relativeDisplacement >= 0f ? relativeDisplacement : ratio >= .5f ? .005f : .01f);
            surfaceError = relativeDisplacement >= 0f;
            // Weld only exactly equivalent authored vertices, never across material/UV/normal seams.
            var membership = new HashSet<int>[n];
            for (int i = 0; i < n; i++) membership[i] = new HashSet<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++) foreach (int v in mesh.GetTriangles(sub)) membership[v].Add(sub);
            var buckets = new Dictionary<int, List<int>>(); var canonical = new int[n];
            for (int v = 0; v < n; v++)
            {
                int hash = data.Hash(v); if (!buckets.TryGetValue(hash, out var bucket)) { bucket = new List<int>(); buckets.Add(hash, bucket); }
                canonical[v] = v;
                foreach (int other in bucket) if (data.Same(v, other) && membership[v].SetEquals(membership[other])) { canonical[v] = other; alive[v] = false; break; }
                if (canonical[v] == v) bucket.Add(v);
            }
            var edges = new Dictionary<Edge, List<int>>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] indices = mesh.GetTriangles(sub);
                for (int t = 0; t < indices.Length; t += 3)
                {
                    var f = new Face { a = canonical[indices[t]], b = canonical[indices[t + 1]], c = canonical[indices[t + 2]], sub = sub };
                    int id = faces.Count; faces.Add(f); triangleCount++;
                    foreach (int v in f.Vertices) incident[v].Add(id);
                    foreach (var e in Edges(f)) { if (!edges.TryGetValue(e, out var list)) { list = new List<int>(); edges.Add(e, list); } list.Add(id); }
                }
            }
            var positions = new Dictionary<Vector3, List<int>>();
            for (int v = 0; v < n; v++) if (alive[v])
            {
                if (!positions.TryGetValue(data.positions[v], out var list)) { list = new List<int>(); positions.Add(data.positions[v], list); }
                list.Add(v);
            }
            foreach (var list in positions.Values) if (list.Count > 1) foreach (int v in list) locked[v] = true;
            foreach (var entry in edges)
            {
                var ids = entry.Value;
                if (ids.Count == 1) { boundary[entry.Key.a] = true; boundary[entry.Key.b] = true; }
                else if (ids.Count != 2 || faces[ids[0]].sub != faces[ids[1]].sub || Vector3.Dot(Normal(faces[ids[0]]), Normal(faces[ids[1]])) < .7071067f)
                { locked[entry.Key.a] = true; locked[entry.Key.b] = true; }
            }
            // Retain extrema, so bounds and the silhouette envelope cannot shrink.
            var bounds = mesh.bounds;
            float epsilon = Mathf.Max(bounds.size.magnitude * 1e-7f, 1e-8f);
            for (int v = 0; v < n; v++) if (alive[v])
            {
                Vector3 p = data.positions[v];
                for (int axis = 0; axis < 3; axis++)
                    if (bounds.size[axis] > epsilon && (Mathf.Abs(p[axis] - bounds.min[axis]) <= epsilon || Mathf.Abs(p[axis] - bounds.max[axis]) <= epsilon)) locked[v] = true;
            }
            foreach (var edge in edges.Keys) Queue(edge.a, edge.b);
        }
        private static IEnumerable<Edge> Edges(Face f) { yield return new Edge(f.a, f.b); yield return new Edge(f.b, f.c); yield return new Edge(f.c, f.a); }
        private Vector3 Normal(Face f) { return Vector3.Cross(data.positions[f.b] - data.positions[f.a], data.positions[f.c] - data.positions[f.a]).normalized; }
        private HashSet<int> Neighbours(int v)
        {
            var set = new HashSet<int>();
            foreach (int id in incident[v]) if (faces[id].alive) foreach (int other in faces[id].Vertices) if (other != v) set.Add(other);
            return set;
        }
        private float CollapseError(int from, int to)
        {
            if (!surfaceError) return radius[from] + Vector3.Distance(data.positions[from], data.positions[to]);
            float error = 0f;
            foreach (int id in incident[from])
            {
                Face face = faces[id];
                if (!face.alive) continue;
                error = Mathf.Max(error, Mathf.Abs(Vector3.Dot(Normal(face), data.positions[to] - data.positions[face.a])));
            }
            return radius[from] + error;
        }
        private void Queue(int a, int b)
        {
            if (!alive[a] || !alive[b] || locked[a] && locked[b]) return;
            int from = locked[a] ? b : a, to = from == a ? b : a;
            if (!locked[b] && !locked[a] && radius[b] > radius[a]) { from = b; to = a; }
            if (boundary[from] && !boundary[to] && !locked[to]) { int swap = from; from = to; to = swap; }
            float distance = Vector3.Distance(data.positions[from], data.positions[to]);
            float error = CollapseError(from, to);
            if (error > maxDisplacement) return;
            heap.Add(new Candidate { from = from, to = to, vf = version[from], vt = version[to], cost = surfaceError ? error + distance * .001f : error });
        }
        private bool CanCollapse(int from, int to)
        {
            if (locked[from] || CollapseError(from, to) > maxDisplacement) return false;
            var commonFaces = incident[from].Where(id => faces[id].alive && faces[id].Has(to)).ToArray();
            if (commonFaces.Length < 1 || commonFaces.Length > 2) return false;
            if (commonFaces.Length == 1)
            {
                if (!boundary[from] || !boundary[to] || incident[from].Count < 2 || incident[to].Count < 2) return false;
            }
            else if (faces[commonFaces[0]].sub != faces[commonFaces[1]].sub || boundary[from] && boundary[to]) return false;
            if (boundary[from] && !boundary[to]) return false;
            var common = Neighbours(from); common.IntersectWith(Neighbours(to));
            var opposite = new HashSet<int>(commonFaces.Select(id => faces[id].Other(from, to)));
            if (opposite.Count != commonFaces.Length || !common.SetEquals(opposite)) return false; // interior/boundary manifold link condition
            for (int i = 0; i < 8; i++) if (data.uv[i].Count == data.positions.Length && (data.uv[i][from] - data.uv[i][to]).sqrMagnitude > .015625f) return false;
            var oppositeEdges = new HashSet<Edge>();
            foreach (int id in incident[to])
            {
                var f = faces[id]; if (!f.alive || f.Has(from)) continue;
                int[] others = f.Vertices.Where(v => v != to).ToArray();
                oppositeEdges.Add(new Edge(others[0], others[1]));
            }
            foreach (int id in incident[from])
            {
                var f = faces[id]; if (!f.alive || f.Has(to)) continue;
                int[] others = f.Vertices.Where(v => v != from).ToArray();
                if (oppositeEdges.Contains(new Edge(others[0], others[1]))) return false; // no duplicate faces/tetrahedron inversion
                Vector3 a = data.positions[f.a == from ? to : f.a], b = data.positions[f.b == from ? to : f.b], c = data.positions[f.c == from ? to : f.c];
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-18f || Vector3.Dot(cross.normalized, Normal(f)) < .8f) return false;
            }
            return true;
        }
        public Mesh Run(float ratio, bool targetVertices = false)
        {
            int target = Mathf.Max(4, Mathf.CeilToInt((targetVertices ? source.vertexCount : triangleCount) * ratio));
            int referencedVertices = incident.Count(set => set.Count > 0);
            while ((targetVertices ? referencedVertices : triangleCount) > target && heap.Count > 0)
            {
                var c = heap.Pop(); int from = c.from, to = c.to;
                if (!alive[from] || !alive[to] || c.vf != version[from] || c.vt != version[to] || !CanCollapse(from, to)) continue;
                float collapseError = CollapseError(from, to);
                var affected = Neighbours(from); affected.UnionWith(Neighbours(to)); affected.Add(to);
                foreach (int id in incident[from].ToArray())
                {
                    var f = faces[id]; if (!f.alive) continue;
                    if (f.Has(to)) { f.alive = false; triangleCount--; foreach (int v in f.Vertices) incident[v].Remove(id); }
                    else { f.Replace(from, to); incident[to].Add(id); }
                }
                incident[from].Clear(); alive[from] = false;
                referencedVertices--;
                foreach (int v in affected)
                    if (alive[v] && incident[v].Count == 0) { alive[v] = false; referencedVertices--; }
                radius[to] = Mathf.Max(radius[to], collapseError);
                foreach (int v in affected) version[v]++;
                version[from]++;
                foreach (int v in affected) if (alive[v]) foreach (int other in Neighbours(v)) Queue(v, other);
            }
            var used = new SortedSet<int>(); foreach (var f in faces) if (f.alive) foreach (int v in f.Vertices) used.Add(v);
            int[] retained = used.ToArray(); var map = new int[data.positions.Length];
            for (int i = 0; i < retained.Length; i++) map[retained[i]] = i;
            var mesh = new Mesh { name = source.name + "_Reduced", indexFormat = retained.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(retained.Select(v => data.positions[v]).ToArray());
            if (data.normals.Length == data.positions.Length) mesh.SetNormals(retained.Select(v => data.normals[v]).ToArray());
            if (data.tangents.Length == data.positions.Length) mesh.SetTangents(retained.Select(v => data.tangents[v]).ToArray());
            if (data.colors.Length == data.positions.Length) mesh.SetColors(retained.Select(v => data.colors[v]).ToArray());
            for (int i = 0; i < 8; i++) if (data.uv[i].Count == data.positions.Length)
            {
                int dimension = source.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + i));
                if (targetVertices && dimension == 2)
                    mesh.SetUVs(i, retained.Select(v => new Vector2(data.uv[i][v].x, data.uv[i][v].y)).ToList());
                else if (targetVertices && dimension == 3)
                    mesh.SetUVs(i, retained.Select(v => new Vector3(data.uv[i][v].x, data.uv[i][v].y, data.uv[i][v].z)).ToList());
                else mesh.SetUVs(i, retained.Select(v => data.uv[i][v]).ToList());
            }
            mesh.subMeshCount = source.subMeshCount;
            for (int sub = 0; sub < source.subMeshCount; sub++) mesh.SetTriangles(faces.Where(f => f.alive && f.sub == sub).SelectMany(f => new[] { map[f.a], map[f.b], map[f.c] }).ToArray(), sub, false);
            mesh.bounds = source.bounds;
            return mesh;
        }
    }
    public static Mesh Reduce(Mesh original, float retainedRatio)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        retainedRatio = Mathf.Clamp(retainedRatio, .01f, 1f);
        if (!original.isReadable) throw new InvalidOperationException("Offline LOD reduction needs readable mesh: " + original.name);
        if (retainedRatio >= 1f || original.vertexCount < 128 || original.blendShapeCount != 0 || original.boneWeights.Length != 0 ||
            Enumerable.Range(0, original.subMeshCount).Any(sub => original.GetTopology(sub) != MeshTopology.Triangles))
            return UnityEngine.Object.Instantiate(original);
        return new Reduction(original, retainedRatio).Run(retainedRatio);
    }
    /// <summary>
    /// Generates an editor-only LOD targeting referenced vertices rather than triangles.
    /// Topology, material/attribute seams and bounded local surface-plane error take precedence
    /// over the requested count. The caller must display the actual result.
    /// </summary>
    public static Mesh ReduceVertices(Mesh original, float retainedRatio)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (float.IsNaN(retainedRatio) || float.IsInfinity(retainedRatio) || retainedRatio <= 0f || retainedRatio > 1f)
            throw new ArgumentOutOfRangeException(nameof(retainedRatio));
        if (!original.isReadable) throw new InvalidOperationException("LOD generation needs a readable mesh: " + original.name);
        if (original.blendShapeCount != 0 || original.bindposes.Length != 0 || original.boneWeights.Length != 0)
            throw new InvalidOperationException("Rigged/blend-shape meshes need a dedicated simplifier: " + original.name);
        if (Enumerable.Range(0, original.subMeshCount).Any(sub => original.GetTopology(sub) != MeshTopology.Triangles))
            throw new InvalidOperationException("LOD generation supports triangle meshes only: " + original.name);
        if (retainedRatio == 1f || original.vertexCount <= 4) return UnityEngine.Object.Instantiate(original);
        return new Reduction(original, retainedRatio, .025f).Run(retainedRatio, true);
    }
}

}
