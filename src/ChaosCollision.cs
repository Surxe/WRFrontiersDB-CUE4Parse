using System.Buffers.Binary;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Readers;

namespace BatchExport
{
    /// <summary>A landscape collision heightfield as the game cooked it (Chaos FHeightField).</summary>
    public sealed class ChaosHeightField
    {
        /// <summary>Vertices along local +Y (rows) and +X (columns); vertex (x, y) is Heights[y * Cols + x].</summary>
        public required int Rows;
        public required int Cols;
        /// <summary>Requantised over this grid's own range: height = MinValue + Heights[i] * HeightPerUnit,
        /// in landscape height units (raw uint16 height minus 32768).</summary>
        public required ushort[] Heights;
        public required double MinValue;
        public required double HeightPerUnit;
        /// <summary>One per cell, (Rows - 1) * (Cols - 1); 255 marks a hole.</summary>
        public required byte[] MaterialIndices;
    }

    /// <summary>A Chaos triangle mesh, its triangles put back into UE's own order (see ChaosCollision).</summary>
    public sealed class ChaosTriangleMesh
    {
        /// <summary>x, y, z per vertex.</summary>
        public required float[] Vertices;
        /// <summary>Three vertex indices per triangle.</summary>
        public required int[] Indices;
    }

    /// <summary>
    /// Decodes the cooked Chaos collision the game ships: landscape heightfields, landscape mesh
    /// collision and body-setup triangle meshes. CUE4Parse reads past these bytes; the layouts here
    /// follow the UE 5.4.4 serializers with the custom versions current for 5.4, which is what cooked
    /// data carries (a Chaos archive over a memory buffer uses the running engine's versions):
    /// <list type="bullet">
    /// <item>pointers: <c>FChaosArchive::SerializePtr</c>, a bool (4 bytes), a tag, then for a new object
    /// <c>FImplicitObject::SerializationFactory</c>'s int8 type and the object;</item>
    /// <item><c>FImplicitObject::SerializeImp</c>: bIsConvex, bDoCollide (bools), CollisionType (int8);</item>
    /// <item><c>FHeightField::FData::Serialize</c> and <c>FTriangleMeshImplicitObject::SerializeImp</c>. One
    /// difference from a reading of the source, checked against the cooked bytes: the heightfield's
    /// Scale vector is stored as three floats although its other reals are doubles.</item>
    /// </list>
    /// Only the leading fields each caller needs are read. Chaos keeps triangles in the opposite order
    /// to the rest of the engine (<c>ChaosCooking.cpp</c> swaps v0 and v1 when it builds a mesh with
    /// bFlipNormals, which every static-mesh and landscape cook sets); they are swapped back here, so
    /// triangle meshes come out in the order of UE's render meshes and the .uemodel files.
    /// </summary>
    public static class ChaosCollision
    {
        private const sbyte TypeTriangleMesh = 11;
        private const sbyte TypeHeightField = 12;

        /// <summary>
        /// The cooked data of a LandscapeHeightfieldCollisionComponent: a bool (simple collision
        /// present), the heightfield, and the simple heightfield when the bool is set.
        /// </summary>
        public static (ChaosHeightField HeightField, bool HasSimple) ReadLandscapeHeightField(byte[] data)
        {
            var reader = new Reader(data);
            var hasSimple = reader.Bool();
            var type = reader.PointerType();
            if (type != TypeHeightField) throw new InvalidDataException($"expected a heightfield (type {TypeHeightField}), found type {type}");
            reader.ImplicitObjectHeader();

            var realSize = reader.Int32();
            var storageSize = reader.Int32();
            if (realSize != 4 && realSize != 8) throw new InvalidDataException($"heightfield real size {realSize}");
            if (storageSize != 2) throw new InvalidDataException($"heightfield storage size {storageSize}, expected uint16");
            var heights = reader.UInt16Array();
            // Scale: three floats whatever the real size (seen in the 2026-11-05 cooked data); (1, 1, 1) when
            // cooked, the component sets the real scale at runtime.
            reader.Reals(4, 3);
            var minValue = reader.Real(realSize);
            reader.Real(realSize); // MaxValue
            int rows = reader.UInt16();
            int cols = reader.UInt16();
            reader.Real(realSize); // Range
            var heightPerUnit = reader.Real(realSize);
            var materials = reader.ByteArray();

            if (heights.Length != rows * cols)
                throw new InvalidDataException($"heightfield has {heights.Length} heights for {rows} x {cols}");
            if (materials.Length != (rows - 1) * (cols - 1))
                throw new InvalidDataException($"heightfield has {materials.Length} cell materials for {rows - 1} x {cols - 1} cells");
            return (new ChaosHeightField
            {
                Rows = rows, Cols = cols, Heights = heights, MinValue = minValue, HeightPerUnit = heightPerUnit,
                MaterialIndices = materials
            }, hasSimple);
        }

        /// <summary>The cooked data of a LandscapeMeshCollisionComponent: one triangle mesh pointer.</summary>
        public static ChaosTriangleMesh ReadLandscapeMesh(byte[] data)
        {
            var reader = new Reader(data);
            var type = reader.PointerType();
            if (type != TypeTriangleMesh) throw new InvalidDataException($"expected a triangle mesh (type {TypeTriangleMesh}), found type {type}");
            return reader.TriangleMesh();
        }

        /// <summary>
        /// The triangle meshes of a body setup's cooked data (<c>FChaosDerivedDataReader</c>): the real
        /// size (4), the convex array, then the triangle mesh array. Only bodies without cooked convex
        /// hulls are decoded (complex-as-simple bodies have none): skipping a convex needs its full
        /// layout. Only the first triangle mesh is decoded; the body setups exported so far have one.
        /// </summary>
        public static ChaosTriangleMesh? ReadBodySetupTriangleMesh(byte[] data, out int meshCount)
        {
            var reader = new Reader(data);
            var realSize = reader.Int32();
            if (realSize != 4) throw new InvalidDataException($"body setup cooked with real size {realSize}");
            var convexCount = reader.Int32();
            if (convexCount != 0) throw new NotSupportedException($"body setup has {convexCount} cooked convex hull(s) before its triangle meshes");
            meshCount = reader.Int32();
            if (meshCount == 0) return null;
            var type = reader.PointerType();
            if (type != TypeTriangleMesh) throw new InvalidDataException($"expected a triangle mesh (type {TypeTriangleMesh}), found type {type}");
            return reader.TriangleMesh();
        }

        private sealed class Reader(byte[] data)
        {
            private int _position;

            private ReadOnlySpan<byte> Take(int count)
            {
                if (count < 0 || _position + count > data.Length)
                    throw new InvalidDataException($"read of {count} bytes at {_position} runs past the end ({data.Length})");
                var span = data.AsSpan(_position, count);
                _position += count;
                return span;
            }

            public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
            public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
            public sbyte Int8() => (sbyte) Take(1)[0];
            public bool Bool() => Int32() != 0;

            public double Real(int size) => size == 8
                ? BinaryPrimitives.ReadDoubleLittleEndian(Take(8))
                : BinaryPrimitives.ReadSingleLittleEndian(Take(4));

            public void Reals(int size, int count) => Take(size * count);

            private int Count(int elementSize)
            {
                var count = Int32();
                if (count < 0 || (long) count * elementSize > data.Length - _position)
                    throw new InvalidDataException($"array of {count} x {elementSize} bytes at {_position} does not fit");
                return count;
            }

            public ushort[] UInt16Array()
            {
                var count = Count(2);
                var span = Take(count * 2);
                var result = new ushort[count];
                for (var i = 0; i < count; i++) result[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(i * 2)..]);
                return result;
            }

            public byte[] ByteArray() => Take(Count(1)).ToArray();

            /// <summary>A new object behind a Chaos pointer: exists, tag, then its implicit-object type.</summary>
            public sbyte PointerType()
            {
                if (!Bool()) throw new InvalidDataException("null Chaos pointer");
                var tag = Int32();
                if (tag != 0) throw new InvalidDataException($"Chaos pointer tag {tag}: only a first, new object is supported");
                return Int8();
            }

            public void ImplicitObjectHeader()
            {
                Bool(); // bIsConvex
                Bool(); // bDoCollide
                Int8(); // CollisionType
            }

            public ChaosTriangleMesh TriangleMesh()
            {
                ImplicitObjectHeader();
                if (!Bool()) throw new InvalidDataException("triangle mesh particles are a shared view");
                var vertexCount = Count(12);
                var vertexSpan = Take(vertexCount * 12);
                var vertices = new float[vertexCount * 3];
                for (var i = 0; i < vertices.Length; i++) vertices[i] = BinaryPrimitives.ReadSingleLittleEndian(vertexSpan[(i * 4)..]);

                var large = Bool();
                var size = large ? 4 : 2;
                var triangleCount = Count(3 * size);
                var indexSpan = Take(triangleCount * 3 * size);
                var indices = new int[triangleCount * 3];
                for (var t = 0; t < triangleCount; t++)
                {
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var at = (t * 3 + corner) * size;
                        var value = large ? BinaryPrimitives.ReadInt32LittleEndian(indexSpan[at..]) : BinaryPrimitives.ReadUInt16LittleEndian(indexSpan[at..]);
                        if (value < 0 || value >= vertexCount) throw new InvalidDataException($"triangle {t} index {value} out of {vertexCount} vertices");
                        // Back to UE's order: swap the first two corners.
                        indices[t * 3 + (corner == 0 ? 1 : corner == 1 ? 0 : 2)] = value;
                    }
                }
                return new ChaosTriangleMesh { Vertices = vertices, Indices = indices };
            }
        }
    }

    /// <summary>
    /// LandscapeHeightfieldCollisionComponent that keeps its cooked collision bytes instead of
    /// skipping them like CUE4Parse's own class. Registered in place of it (MapExporter).
    /// </summary>
    public class CookedLandscapeCollisionComponent : USceneComponent
    {
        public byte[]? CookedCollisionData;

        public override void Deserialize(FAssetArchive Ar, long validPos)
        {
            base.Deserialize(Ar, validPos);
            // ULandscapeHeightfieldCollisionComponent::Serialize: bCooked, then the cooked data as a bulk array.
            if (Ar.ReadBoolean())
                CookedCollisionData = Ar.ReadBulkArray<byte>();
        }
    }

    /// <summary>LandscapeMeshCollisionComponent: the same layout (its triangle mesh is in the cooked
    /// data the parent class serializes), followed by a bool of its own that carries nothing when cooked.</summary>
    public sealed class CookedLandscapeMeshCollisionComponent : CookedLandscapeCollisionComponent;
}
