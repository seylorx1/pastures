using System;
using System.Data;

namespace Pastures;
public struct TerrainQuadCell
{
    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public enum Edge
    {
        Up      = 0b0001,
        Down    = 0b0010,
        Left    = 0b0100,
        Right   = 0b1000
    }

    public int[] uniformCornerIndex;
    public int[] cornerIndex;

    public int edges;

    public TerrainQuadCell(int topLeft, int topRight, int bottomLeft, int bottomRight)
    {
        uniformCornerIndex = new int[]
        {
            topLeft,
            topRight,
            bottomLeft,
            bottomRight
        };

        cornerIndex = new int[]
        {
            -1,
            -1,
            -1,
            -1
        };

        edges = 0;
    }

    public void SetExternalEdge(Edge edge)
    {
        edges |= (int)edge;
    }

    public readonly bool IsCornerVertex(Corner corner)
    {
        // This quad cell has no connected sides, so it is a standalone quad.
        // All corners should be verts.
        if(IsCellSingleQuad())
            return true;

        // This quad cell has all sides connected, meaning it is in the middle of a quad.
        // No corners should be verts.
        if(edges == 0b1111)
            return false;

        return corner switch
        {
            Corner.TopLeft      => (edges & 0b0101) == 0b0101,
            Corner.TopRight     => (edges & 0b1001) == 0b1001,
            Corner.BottomLeft   => (edges & 0b0110) == 0b0110,
            Corner.BottomRight  => (edges & 0b1010) == 0b1010,
            _ => false
        };
    }

    public readonly bool IsCellSingleQuad() => edges == 0;
}