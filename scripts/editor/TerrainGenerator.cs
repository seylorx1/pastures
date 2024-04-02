using Godot;
using System.Collections.Generic;

namespace Pastures;

[Tool]
public partial class TerrainGenerator : MeshInstance3D
{
    private Vector2 _size = new(32.0f, 32.0f);
    private int _resolution = 128;
    private int _chunkCount = 4;

    [Export] Texture2D Heightmap { get; set; }

    [Export]
    public Vector2 Size
    {
        get => _size;

        set
        {
            _size = new
            (
                x: Mathf.Max(value.X, 1),
                y: Mathf.Max(value.Y, 1)
            );
        }
    }

    [Export]
    public int Resolution
    {
        get => _resolution;

        // Make sure resolution is never negative, and is always divisible by 2.
        set
        {
            // Always enforce a minimum size of 32. (Do while will bitshift this 16 to 32)
            _resolution = 16;

            // Bitshift clamp between 32 and 8196 (although this upper value will likely be utterly overkill).
            do
            {
                _resolution <<= 1;
            }
            while (_resolution < value && _resolution < 8192);
        }
    }

    [Export]
    public int ChunkCount
    {
        get => _chunkCount;

        set
        {
            // Minimum chunk count is two. (Do while will bitshift this 1 to a 2.)
            _chunkCount = 1;

            // Find the next lowest power of two from the value by bitshifting up.
            do
            {
                _chunkCount <<= 1;
            }
            while (_chunkCount < value && _chunkCount < _resolution >> 1);

            // Value is either half of resolution or the next lowest power of two to value.
        }
    }

    [Export] public float Amplitude { get; set; } = 16.0f;
    [Export] public float SimplifyThreshold { get; set; } = 0.005f;
    [Export] public bool ShowChunksInHierarchy {get; set; } = false;

    // Buttons
    [Export]
    public bool GenerateMesh
    {
        get => false;
        set
        {
            if (value)
                OnGenerateMesh();
        }
    }

    public void OnGenerateMesh()
    {
        // Remove any pre-existing child nodes.
        while(GetChildCount(true) > 0)
        {
            GetChild(0, true).Free();
        }

        // Create chunk nodes.
        int chunkCount = ChunkCount; // Cache result.
        Vector2 chunkSize = _size / chunkCount;
        for(int z = 0; z < chunkCount; z++)
        {
            for(int x = 0; x < chunkCount; x++)
            {
                // Calculate chunk centre.
                Vector3 chunkPos =
                    new
                    (
                        x: (x - chunkCount / 2) * chunkSize.X + chunkSize.X / 2,
                        y: 0,
                        z: (z - chunkCount / 2) * chunkSize.Y + chunkSize.Y / 2
                    );

                // Create node at position
                MeshInstance3D meshInstance = new()
                {
                    Name = $"Chunk ({x}, {z})",
                    Position = chunkPos
                };

                // Add to scene tree.
                AddChild(meshInstance, false, ShowChunksInHierarchy ? InternalMode.Disabled : InternalMode.Back);

                if(ShowChunksInHierarchy)
                    meshInstance.Owner = GetTree().EditedSceneRoot;

                // Create thread to split mesh to new array mesh.
            }
        }

        Image heightmapImage = null;

        // Sample heightmap
        if (Heightmap != null)
            heightmapImage = Heightmap.GetImage();

        // Init arrays.
        Godot.Collections.Array surfaceArray = new();
        surfaceArray.Resize((int)Mesh.ArrayType.Max);

        TerrainMeshData data = new(heightmapImage, _resolution, _chunkCount, _size, Amplitude, SimplifyThreshold);

        // Apply lists to arrays.
        surfaceArray[(int)Mesh.ArrayType.Vertex] = data.Vertices.ToArray();
        surfaceArray[(int)Mesh.ArrayType.TexUV] = data.UVs.ToArray();
        surfaceArray[(int)Mesh.ArrayType.Normal] = data.Normals;
        surfaceArray[(int)Mesh.ArrayType.Index] = data.Indices.ToArray();

        // Set the mesh.
        ArrayMesh arrMesh = new();
        arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
        Mesh = arrMesh;
    }

}