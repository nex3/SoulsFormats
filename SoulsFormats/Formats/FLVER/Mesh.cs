﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SoulsFormats
{
    public partial class FLVER
    {
        /// <summary>
        /// An individual chunk of a model.
        /// </summary>
        public class Mesh
        {
            /// <summary>
            /// Unknown.
            /// </summary>
            public bool Dynamic { get; set; }

            /// <summary>
            /// Index of the material used by all triangles in this mesh.
            /// </summary>
            public int MaterialIndex { get; set; }

            /// <summary>
            /// Apparently does nothing. Usually points to a dummy bone named after the model, possibly just for labelling.
            /// </summary>
            public int DefaultBoneIndex { get; set; }

            /// <summary>
            /// Indexes of bones in the bone collection which may be used by vertices in this mesh.
            /// </summary>
            public List<int> BoneIndices { get; set; }

            /// <summary>
            /// Triangles in this mesh.
            /// </summary>
            public List<FaceSet> FaceSets { get; set; }

            /// <summary>
            /// Vertex buffers in this mesh.
            /// </summary>
            public List<VertexBuffer> VertexBuffers { get; set; }

            /// <summary>
            /// Vertices in this mesh.
            /// </summary>
            public List<Vertex> Vertices { get; set; }

            /// <summary>
            /// Optional bounding box struct; may be null.
            /// </summary>
            public BoundingBoxes BoundingBox { get; set; }

            private int[] faceSetIndices, vertexBufferIndices;

            /// <summary>
            /// Creates a new Mesh with default values.
            /// </summary>
            public Mesh()
            {
                Dynamic = false;
                MaterialIndex = 0;
                DefaultBoneIndex = -1;
                BoneIndices = new List<int>();
                FaceSets = new List<FaceSet>();
                VertexBuffers = new List<VertexBuffer>();
                Vertices = new List<Vertex>();
            }

            internal Mesh(BinaryReaderEx br, int version)
            {
                Dynamic = br.ReadBoolean();
                br.AssertByte(0);
                br.AssertByte(0);
                br.AssertByte(0);

                MaterialIndex = br.ReadInt32();
                br.AssertInt32(0);
                br.AssertInt32(0);
                DefaultBoneIndex = br.ReadInt32();
                int boneCount = br.ReadInt32();
                int boundingBoxOffset = br.ReadInt32();
                int boneOffset = br.ReadInt32();
                int faceSetCount = br.ReadInt32();
                int faceSetOffset = br.ReadInt32();
                int vertexBufferCount = br.AssertInt32(1, 2, 3);
                int vertexBufferOffset = br.ReadInt32();

                if (boundingBoxOffset != 0)
                {
                    br.StepIn(boundingBoxOffset);
                    {
                        BoundingBox = new BoundingBoxes(br, version);
                    }
                    br.StepOut();
                }

                BoneIndices = new List<int>(br.GetInt32s(boneOffset, boneCount));
                faceSetIndices = br.GetInt32s(faceSetOffset, faceSetCount);
                vertexBufferIndices = br.GetInt32s(vertexBufferOffset, vertexBufferCount);
            }

            internal void TakeFaceSets(Dictionary<int, FaceSet> faceSetDict)
            {
                FaceSets = new List<FaceSet>(faceSetIndices.Length);
                foreach (int i in faceSetIndices)
                {
                    if (!faceSetDict.ContainsKey(i))
                        throw new NotSupportedException("Face set not found or already taken: " + i);

                    FaceSets.Add(faceSetDict[i]);
                    faceSetDict.Remove(i);
                }
                faceSetIndices = null;
            }

            internal void TakeVertexBuffers(Dictionary<int, VertexBuffer> vertexBufferDict, List<BufferLayout> layouts)
            {
                VertexBuffers = new List<VertexBuffer>(vertexBufferIndices.Length);
                foreach (int i in vertexBufferIndices)
                {
                    if (!vertexBufferDict.ContainsKey(i))
                        throw new NotSupportedException("Vertex buffer not found or already taken: " + i);

                    VertexBuffers.Add(vertexBufferDict[i]);
                    vertexBufferDict.Remove(i);
                }
                vertexBufferIndices = null;

                // Make sure no semantics repeat that aren't known to
                var semantics = new List<BufferLayout.MemberSemantic>();
                foreach (VertexBuffer buffer in VertexBuffers)
                {
                    foreach (var member in layouts[buffer.LayoutIndex])
                    {
                        if (member.Semantic != BufferLayout.MemberSemantic.UV
                            && member.Semantic != BufferLayout.MemberSemantic.Tangent
                            && member.Semantic != BufferLayout.MemberSemantic.VertexColor
                            && member.Semantic != BufferLayout.MemberSemantic.Position
                            && member.Semantic != BufferLayout.MemberSemantic.Normal)
                        {
                            if (semantics.Contains(member.Semantic))
                                throw new NotImplementedException("Unexpected semantic list.");
                            semantics.Add(member.Semantic);
                        }
                    }
                }

                for (int i = 0; i < VertexBuffers.Count; i++)
                {
                    VertexBuffer buffer = VertexBuffers[i];
                    if (buffer.BufferIndex != i)
                        throw new FormatException("Unexpected vertex buffer indices.");

                    BufferLayout layout = layouts[buffer.LayoutIndex];
                    if (layout.Size != buffer.VertexSize)
                        throw new FormatException("Mismatched vertex sizes are not supported for split buffers.");
                }
            }

            internal void ReadVertices(BinaryReaderEx br, int dataOffset, List<BufferLayout> layouts, int version)
            {
                var layoutMembers = layouts.SelectMany(l => l);
                int uvCap = layoutMembers.Where(m => m.Semantic == BufferLayout.MemberSemantic.UV).Count();
                int tanCap = layoutMembers.Where(m => m.Semantic == BufferLayout.MemberSemantic.Tangent).Count();
                int colorCap = layoutMembers.Where(m => m.Semantic == BufferLayout.MemberSemantic.VertexColor).Count();

                int vertexCount = VertexBuffers[0].VertexCount;
                Vertices = new List<Vertex>(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                    Vertices.Add(new Vertex(uvCap, tanCap, colorCap));

                foreach (VertexBuffer buffer in VertexBuffers)
                    buffer.ReadBuffer(br, layouts, Vertices, dataOffset, version);
            }

            internal void Write(BinaryWriterEx bw, int index, int version)
            {
                bw.WriteBoolean(Dynamic);
                bw.WriteByte(0);
                bw.WriteByte(0);
                bw.WriteByte(0);

                bw.WriteInt32(MaterialIndex);
                bw.WriteInt32(0);
                bw.WriteInt32(0);
                bw.WriteInt32(DefaultBoneIndex);
                bw.WriteInt32(BoneIndices.Count);
                bw.ReserveInt32($"MeshBoundingBox{index}");
                bw.ReserveInt32($"MeshBoneIndices{index}");
                bw.WriteInt32(FaceSets.Count);
                bw.ReserveInt32($"MeshFaceSetIndices{index}");
                bw.WriteInt32(VertexBuffers.Count);
                bw.ReserveInt32($"MeshVertexBufferIndices{index}");
            }

            internal void WriteBoundingBox(BinaryWriterEx bw, int index, int version)
            {
                if (BoundingBox == null)
                {
                    bw.FillInt32($"MeshBoundingBox{index}", 0);
                }
                else
                {
                    bw.FillInt32($"MeshBoundingBox{index}", (int)bw.Position);
                    BoundingBox.Write(bw, version);
                }
            }

            internal void WriteBoneIndices(BinaryWriterEx bw, int index, int boneIndicesStart)
            {
                if (BoneIndices.Count == 0)
                {
                    // Just a weird case for byte-perfect writing
                    bw.FillInt32($"MeshBoneIndices{index}", boneIndicesStart);
                }
                else
                {
                    bw.FillInt32($"MeshBoneIndices{index}", (int)bw.Position);
                    bw.WriteInt32s(BoneIndices);
                }
            }

            /// <summary>
            /// Returns a list of arrays of 3 vertices, each representing a triangle in the mesh.
            /// Faces are taken from the first FaceSet in the mesh with the given flags,
            /// using None by default for the highest detail mesh. If not found, the first FaceSet is used.
            /// </summary>
            public List<Vertex[]> GetFaces(FaceSet.FSFlags fsFlags = FaceSet.FSFlags.None)
            {
                FaceSet faceset = FaceSets.Find(fs => fs.Flags == fsFlags) ?? FaceSets[0];
                List<int[]> indices = faceset.GetFaces(Vertices.Count < ushort.MaxValue);
                var vertices = new List<Vertex[]>(indices.Count);
                foreach (int[] face in indices)
                    vertices.Add(new Vertex[] { Vertices[face[0]], Vertices[face[1]], Vertices[face[2]] });
                return vertices;
            }

            /// <summary>
            /// An optional bounding box for meshes added in DS2.
            /// </summary>
            public class BoundingBoxes
            {
                /// <summary>
                /// Minimum extent of the mesh.
                /// </summary>
                public Vector3 Min { get; set; }

                /// <summary>
                /// Maximum extent of the mesh.
                /// </summary>
                public Vector3 Max { get; set; }

                /// <summary>
                /// Unknown; only present in Sekiro.
                /// </summary>
                public Vector3 Unk { get; set; }

                internal BoundingBoxes(BinaryReaderEx br, int version)
                {
                    Min = br.ReadVector3();
                    Max = br.ReadVector3();
                    if (version >= 0x2001A)
                        Unk = br.ReadVector3();
                }

                internal void Write(BinaryWriterEx bw, int version)
                {
                    bw.WriteVector3(Min);
                    bw.WriteVector3(Max);
                    if (version >= 0x2001A)
                        bw.WriteVector3(Unk);
                }
            }
        }
    }
}
