﻿using ColossalFramework.Importers;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using ColossalFramework.Packaging;
using ColossalFramework;

namespace LoadingScreenMod
{
    internal sealed class AssetDeserializer
    {
        readonly Package package;
        readonly PackageReader reader;
        int ind = 0;

        public static T Instantiate<T>(Package.Asset asset, int ind = 0) where T : class
        {
            return Instantiate(asset, ind) as T;
        }

        public static object Instantiate(Package.Asset asset, int ind = 0)
        {
            using (Stream stream = Sharing.instance.GetStream(asset))
            using (PackageReader reader = Sharing.instance.GetReader(stream))
            {
                AssetDeserializer d = new AssetDeserializer(asset, reader);
                d.ind = ind;
                Trace.Tra("Deserialize");
                Trace.Typ(asset.GetType());
                Trace.Ind(ind, "ASSET", asset.fullName);
                return d.Deserialize();
            }
        }

        AssetDeserializer(Package.Asset asset, PackageReader reader)
        {
            this.package = asset.package;
            this.reader = reader;
        }

        object Deserialize()
        {
            ind++;
            Type type;

            if (!DeserializeHeader(out type))
                return null;

            Trace.Typ(type);

            if (type == typeof(GameObject))
                return DeserializeGameObject();
            if (type == typeof(Mesh))
                return DeserializeMesh();
            if (type == typeof(Material))
                return DeserializeMaterial();
            if (type == typeof(Texture2D) || type == typeof(Image))
                return DeserializeTexture();
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return DeserializeScriptableObject(type);

            return DeserializeObject(type);
        }

        object DeserializeSingleObject(Type type, Type expectedType)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            Trace.Typ(type);

            // Make the common case fast.
            if (type == typeof(float))
                return reader.ReadSingle();
            if (type == typeof(Vector2))
                return reader.ReadVector2();

            object obj = CustomDeserialize(type);

            if (obj != null)
                return obj;
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return Instantiate(ReadAsset(), ind);
            if (typeof(GameObject).IsAssignableFrom(type))
                return Instantiate(ReadAsset(), ind);

            Trace.Tra("ReadUnityType");

            if (package.version < 3u && expectedType != null && expectedType == typeof(Package.Asset))
                return reader.ReadUnityType(expectedType);

            return reader.ReadUnityType(type);
        }

        UnityEngine.Object DeserializeScriptableObject(Type type)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            object obj = CustomDeserialize(type);

            if (obj != null)
                return (UnityEngine.Object) obj;

            ScriptableObject so = ScriptableObject.CreateInstance(type);
            so.name = reader.ReadString();
            Trace.Ind(ind, "SO", so.name);
            DeserializeFields(so, type, false);
            return so;
        }

        void DeserializeFields(object obj, Type type, bool resolveMember)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            int count = reader.ReadInt32();
            ind++;

            for (int i = 0; i < count; i++)
            {
                Type t;
                string name;

                if (DeserializeHeader(out t, out name))
                {
                    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (field == null && resolveMember)
                        field = type.GetField(ResolveLegacyMember(t, type, name), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    Type expectedType = field?.FieldType;

                    if (t.IsArray)
                    {
                        int n = reader.ReadInt32();
                        Array array = Array.CreateInstance(t.GetElementType(), n);

                        for (int j = 0; j < n; j++)
                            array.SetValue(DeserializeSingleObject(t.GetElementType(), expectedType), j);

                        field?.SetValue(obj, array);
                    }
                    else
                    {
                        object value = DeserializeSingleObject(t, expectedType);
                        field?.SetValue(obj, value);
                    }
                }
            }

            ind--;
        }

        UnityEngine.Object DeserializeGameObject()
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            string name = reader.ReadString();
            GameObject go = new GameObject(name);
            go.tag = reader.ReadString();
            go.layer = reader.ReadInt32();
            go.SetActive(reader.ReadBoolean());
            int count = reader.ReadInt32();
            Trace.Ind(ind, "GO", go.name + ", " + count);

            ind++;
            for (int i = 0; i < count; i++)
            {
                Type type;

                if (!DeserializeHeader(out type))
                    continue;

                Trace.Typ(type);

                if (type == typeof(Transform))
                    DeserializeTransform(go.transform);
                else if (type == typeof(MeshFilter))
                    DeserializeMeshFilter(go.AddComponent(type) as MeshFilter);
                else if (type == typeof(MeshRenderer))
                    DeserializeMeshRenderer(go.AddComponent(type) as MeshRenderer);
                else if (typeof(MonoBehaviour).IsAssignableFrom(type))
                    DeserializeMonoBehaviour((MonoBehaviour) go.AddComponent(type));
                else
                    throw new InvalidDataException("Unknown type to deserialize " + type.Name);
            }

            ind--;
            return go;
        }

        UnityEngine.Object DeserializeTexture()
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            string name = reader.ReadString();
            bool linear = reader.ReadBoolean();
            int count = reader.ReadInt32();

            Trace.texBytes -= Profiling.Micros;
            byte[] fileByte = reader.ReadBytes(count);

            long m = Profiling.Micros;
            Trace.texBytes += m;
            Trace.texImage -= m;

            Image image = new Image(fileByte);

            m = Profiling.Micros;
            Trace.texImage += m;
            Trace.texCreate -= m;

            Texture2D texture2D = image.CreateTexture(linear);
            Trace.texCreate += Profiling.Micros;

            texture2D.name = name;
            Trace.Ind(ind, "Texture2D", name, texture2D.width, "x", texture2D.height);
            return texture2D;
        }

        UnityEngine.Object DeserializeMaterial()
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            string name = reader.ReadString();
            string shader = reader.ReadString();
            Material material = new Material(Shader.Find(shader));
            material.name = name;
            int count = reader.ReadInt32();
            Trace.Ind(ind, "Material", name + ", " + shader);
            ind++;

            for (int i = 0; i < count; i++)
            {
                int num2 = reader.ReadInt32();
                if (num2 == 0)
                {
                    material.SetColor(reader.ReadString(), reader.ReadColor());
                }
                else if (num2 == 1)
                {
                    material.SetVector(reader.ReadString(), reader.ReadVector4());
                }
                else if (num2 == 2)
                {
                    material.SetFloat(reader.ReadString(), reader.ReadSingle());
                }
                else if (num2 == 3)
                {
                    string propertyName = reader.ReadString();
                    if (!reader.ReadBoolean())
                    {
                        material.SetTexture(propertyName, Instantiate<Texture>(ReadAsset(), ind));
                    }
                    else
                    {
                        material.SetTexture(propertyName, null);
                    }
                }
            }

            ind--;
            return material;
        }

        void DeserializeTransform(Transform transform)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            transform.localPosition = reader.ReadVector3();
            transform.localRotation = reader.ReadQuaternion();
            transform.localScale = reader.ReadVector3();
            Trace.Ind(ind, "Transform", transform.name);
        }

        void DeserializeMeshFilter(MeshFilter meshFilter)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            meshFilter.sharedMesh = Instantiate<Mesh>(ReadAsset(), ind);
        }

        void DeserializeMonoBehaviour(MonoBehaviour behaviour)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            Trace.Ind(ind, behaviour.GetType().Name);
            DeserializeFields(behaviour, behaviour.GetType(), false);
        }

        object DeserializeObject(Type type)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            Trace.Ind(ind, "object", type.Name);
            object obj = CustomDeserialize(type);

            if (obj != null)
                return obj;

            obj = Activator.CreateInstance(type);
            reader.ReadString();
            DeserializeFields(obj, type, true);
            return obj;
        }

        void DeserializeMeshRenderer(MeshRenderer renderer)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            int num = reader.ReadInt32();
            Material[] array = new Material[num];

            for (int i = 0; i < num; i++)
                array[i] = Instantiate<Material>(ReadAsset(), ind);

            renderer.sharedMaterials = array;
        }

        UnityEngine.Object DeserializeMesh()
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            Trace.meshMicros -= Profiling.Micros;
            Mesh mesh = new Mesh();
            mesh.name = reader.ReadString();
            mesh.vertices = reader.ReadVector3Array();
            mesh.colors = reader.ReadColorArray();
            mesh.uv = reader.ReadVector2Array();
            mesh.normals = reader.ReadVector3Array();
            mesh.tangents = reader.ReadVector4Array();
            mesh.boneWeights = reader.ReadBoneWeightsArray();
            mesh.bindposes = reader.ReadMatrix4x4Array();
            mesh.subMeshCount = reader.ReadInt32();

            for (int i = 0; i < mesh.subMeshCount; i++)
                mesh.SetTriangles(reader.ReadInt32Array(), i);

            Trace.meshMicros += Profiling.Micros;
            Trace.Ind(ind, "Mesh", mesh.name + ", " + mesh.vertexCount + ", " + mesh.triangles.Length);
            return mesh;
        }

        bool DeserializeHeader(out Type type)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            type = null;

            if (reader.ReadBoolean())
                return false;

            string text = reader.ReadString();
            type = Type.GetType(text);

            if (type == null)
            {
                type = Type.GetType(ResolveLegacyType(text));

                if (type == null)
                {
                    if (HandleUnknownType(text) < 0)
                        throw new InvalidDataException("Unknown type to deserialize " + text);

                    return false;
                }
            }

            return true;
        }

        Package.Asset ReadAsset()
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            return package.FindByChecksum(reader.ReadString());
        }

        bool DeserializeHeader(out Type type, out string name)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            type = null;
            name = null;

            if (reader.ReadBoolean())
                return false;

            string text = reader.ReadString();
            type = Type.GetType(text);
            name = reader.ReadString();

            if (type == null)
            {
                type = Type.GetType(ResolveLegacyType(text));

                if (type == null)
                {
                    if (HandleUnknownType(text) < 0)
                        throw new InvalidDataException("Unknown type to deserialize " + text);

                    return false;
                }
            }

            return true;
        }

        int HandleUnknownType(string type)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            int num = PackageHelper.UnknownTypeHandler(type);
            CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.Packer, string.Concat("Unexpected type '", type, "' detected. No resolver handled this type. Skipping ", num, " bytes."));

            if (num > 0)
            {
                reader.ReadBytes(num);
                return num;
            }
            return -1;
        }

        static string ResolveLegacyType(string type)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            string text = PackageHelper.ResolveLegacyTypeHandler(type);
            CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.Packer, string.Concat("Unkown type detected. Attempting to resolve from '", type, "' to '", text, "'"));
            return text;
        }

        static string ResolveLegacyMember(Type fieldType, Type classType, string member)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);
            string text = PackageHelper.ResolveLegacyMemberHandler(classType, member);
            CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.Packer, string.Concat("Unkown member detected of type ", fieldType.FullName, " in ", classType.FullName,
                ". Attempting to resolve from '", member, "' to '", text, "'"));
            return text;
        }

        object CustomDeserialize(Type type)
        {
            Trace.Tra(MethodBase.GetCurrentMethod().Name);

            if (type == typeof(Package.Asset))
                return ReadAsset();

            return PackageHelper.CustomDeserialize(package, type, reader);
        }
    }
}