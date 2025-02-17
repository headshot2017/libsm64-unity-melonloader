﻿using MelonLoader;
using LibSM64;
using UnityEngine;
using System.Security.Cryptography;
using System.Text;

// Do not copy these [assembly] lines.
// Instead, use the MelonLoader wizard on Visual Studio on the game you want to mod.
// It will generate the MelonGame for the chosen game.
[assembly: MelonInfo(typeof(SM64Mod.Core), "libsm64 MelonLoader", "1.0.0", "Headshotnoby/headshot2017", null)]
[assembly: MelonGame("Author", "Name")]

namespace SM64Mod
{
    public class Core : MelonMod
    {
        static List<SM64Mario> _marios = new List<SM64Mario>();
        static List<SM64DynamicTerrain> _surfaceObjects = new List<SM64DynamicTerrain>();

        public override void OnInitializeMelon()
        {
            byte[] rom;

            try
            {
                rom = File.ReadAllBytes("sm64.z64");
            }
            catch (FileNotFoundException)
            {
                LoggerInstance.Msg("Super Mario 64 US ROM 'sm64.z64' not found");
                return;
            }

            using (var cryptoProvider = new SHA1CryptoServiceProvider())
            {
                byte[] hash = cryptoProvider.ComputeHash(rom);
                StringBuilder result = new StringBuilder(4 * 2);

                for (int i = 0; i < 4; i++)
                    result.Append(hash[i].ToString("x2"));

                string hashStr = result.ToString();

                if (hashStr != "9bef1128")
                {
                    LoggerInstance.Msg($"Super Mario 64 US ROM 'sm64.z64' SHA-1 mismatch\nExpected: 9bef1128\nYour copy: {hashStr}\n\nPlease supply the correct ROM.");
                    return;
                }
            }

            Interop.GlobalInit(rom);
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            _surfaceObjects.Clear();
            _marios.Clear();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LoggerInstance.Msg($"{buildIndex} {sceneName}");

            /*
             * At this point, there's a bunch of things to do:
             * 
             * First, you need to find the scene where the actual gameplay takes place,
             * and make sure to spawn Mario in that scene only.
             *
             * After you find it, You need to find the game's collision surfaces,
             * either MeshCollider or BoxCollider objects, and convert them into SM64 static terrain.
             * You can find these objects with "GameObject.FindObjectsOfType<>();"
             * then create the surfaceObj GameObject and add the SM64StaticTerrain component to it.
             * For moving platforms or destructible terrain, you should use SM64DynamicTerrain instead.
             * 
             * You also need to make a class that inherits from SM64InputProvider.
             * This is where SM64Mario will read the game's input presses.
             * Typically you'll want to read the player object's input.
             * 
             * Once that is said and done, create a new GameObject,
             * and add the SM64Mario component and your SM64 input provider class as a component to it.
             * You also need to give Mario an Unity Material object. Usually you can just borrow this from the player object.
             * 
             * I'll leave behind an example that does all this.
             * You'll need to adapt it to the game you're modding.
             */


            if (buildIndex == -1)
            {
                MeshCollider[] meshCols = GameObject.FindObjectsOfType<MeshCollider>();
                BoxCollider[] boxCols = GameObject.FindObjectsOfType<BoxCollider>();

                for (int i = 0; i < meshCols.Length; i++)
                {
                    MeshCollider c = meshCols[i];
                    if (c.isTrigger)
                        continue;

                    GameObject surfaceObj = new GameObject($"SM64_SURFACE_MESH ({c.name})");
                    MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                    surfaceObj.AddComponent<SM64StaticTerrain>();
                    surfaceObj.transform.rotation = c.transform.rotation;
                    surfaceObj.transform.position = c.transform.position;

                    List<int> tris = new List<int>();
                    for (int j=0; j<c.sharedMesh.subMeshCount; j++)
                    {
                        int[] sub = c.sharedMesh.GetTriangles(j);
                        for (int k=0; k<sub.Length; k++)
                            tris.Add(sub[k]);
                    }

                    Mesh mesh = new Mesh();
                    mesh.name = $"SM64_MESH {i}";
                    mesh.SetVertices(c.sharedMesh.vertices);
                    mesh.SetTriangles(tris, 0);

                    surfaceMesh.sharedMesh = mesh;
                }

                for (var i=0; i<boxCols.Length; i++)
                {
                    // This isn't perfect but it kinda works for now
                    BoxCollider c = boxCols[i];
                    if (c.isTrigger)
                        continue;

                    GameObject surfaceObj = new GameObject($"SM64_SURFACE_BOX ({c.name})");
                    MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                    surfaceObj.AddComponent<SM64StaticTerrain>();

                    Mesh mesh = new Mesh();
                    mesh.name = $"SM64_MESH {i}";
                    mesh.SetVertices(GetColliderVertexPositions(c));
                    mesh.SetTriangles(new int[] {
                        // min Y
                        0, 1, 4,
                        5, 4, 1,

                        // max Y
                        2, 3, 6,
                        7, 6, 3,

                        /*
                        // min X
                        2, 1, 0,
                        1, 2, 3,

                        // max X
                        4, 5, 6,
                        7, 6, 5,

                        // min Z
                        4, 2, 0,
                        2, 4, 6,
                        */
                    }, 0);
                    surfaceMesh.sharedMesh = mesh;
                }
                RefreshStaticTerrain();

                // "p" is the player object/component in this case.
                // You'll need to get this object yourself
                if (p != null)
                {
                    Renderer[] r = p.GetComponentsInChildren<Renderer>();
                    Material material = null;
                    for (int i=0; i<r.Length; i++)
                    {
                        LoggerInstance.Msg($"MAT NAME {i} '{r[i].material.name}' '{r[i].material.shader.name}'");

                        // Make the original player object invisible by forcing the material to not render
                        r[i].forceRenderingOff = true;

                        // Change this with the shader that you want. You'll have to play around a bit
                        if (material == null && r[i].material.shader.name.StartsWith("Toony Colors Pro 2"))
                            material = Material.Instantiate<Material>(r[i].material);
                    }

                    if (material != null)
                    {
                        material.SetTexture("_BaseMap", Interop.marioTexture);
                        material.SetColor("_BaseColor", Color.white);
                    }

                    // Uncomment this to create a test SM64 surface at the player's spawn position
                    /*
                    Vector3 P = p.transform.position;
                    P.y -= 2;
                    GameObject surfaceObj = new GameObject("SM64_SURFACE");
                    MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                    surfaceObj.AddComponent<SM64StaticTerrain>();
                    Mesh mesh = new Mesh();
                    mesh.name = "TEST_MESH";
                    mesh.SetVertices(
                        new Vector3[]
                        {
                            new Vector3(P.x-128,P.y,P.z-128), new Vector3(P.x+128,P.y,P.z+128), new Vector3(P.x+128,P.y,P.z-128),
                            new Vector3(P.x+128,P.y,P.z+128), new Vector3(P.x-128,P.y,P.z-128), new Vector3(P.x-128,P.y,P.z+128),
                        }
                    );
                    mesh.SetTriangles(new int[] { 0, 1, 2, 3, 4, 5 }, 0);
                    surfaceMesh.sharedMesh = mesh;
                    RefreshStaticTerrain();
                    */

                    GameObject marioObj = new GameObject("SM64_MARIO");
                    marioObj.transform.position = p.transform.position;
                    LoggerInstance.Msg($"spawn {p.transform.position.x} {p.transform.position.y} {p.transform.position.z}");
                    SM64InputGame input = marioObj.AddComponent<SM64InputGame>();
                    SM64Mario mario = marioObj.AddComponent<SM64Mario>();
                    if (mario.spawned)
                    {
                        mario.SetMaterial(material);
                        RegisterMario(mario);

                        p.enabled = false;
                    }
                    else
                        LoggerInstance.Msg("Failed to spawn Mario");
                }
            }
        }

        public override void OnUpdate()
        {
            foreach (var o in _surfaceObjects)
                o.contextUpdate();

            foreach (var o in _marios)
                o.contextUpdate();
        }

        public override void OnFixedUpdate()
        {
            foreach (var o in _surfaceObjects)
                o.contextFixedUpdate();

            foreach (var o in _marios)
                o.contextFixedUpdate();
        }

        public override void OnApplicationQuit()
        {
            Interop.GlobalTerminate();
        }

        public void RefreshStaticTerrain()
        {
            Interop.StaticSurfacesLoad(Utils.GetAllStaticSurfaces());
        }

        public void RegisterMario(SM64Mario mario)
        {
            if (!_marios.Contains(mario))
                _marios.Add(mario);
        }

        public void UnregisterMario(SM64Mario mario)
        {
            if (_marios.Contains(mario))
                _marios.Remove(mario);
        }

        public void RegisterSurfaceObject(SM64DynamicTerrain surfaceObject)
        {
            if (!_surfaceObjects.Contains(surfaceObject))
                _surfaceObjects.Add(surfaceObject);
        }

        public void UnregisterSurfaceObject(SM64DynamicTerrain surfaceObject)
        {
            if (_surfaceObjects.Contains(surfaceObject))
                _surfaceObjects.Remove(surfaceObject);
        }

        Vector3[] GetColliderVertexPositions(BoxCollider col)
        {
            var trans = col.transform;
            var min = (col.center - col.size * 0.5f);
            var max = (col.center + col.size * 0.5f);

            Vector3 savedPos = trans.position;

            var P000 = trans.TransformPoint(new Vector3(min.x, min.y, min.z));
            var P001 = trans.TransformPoint(new Vector3(min.x, min.y, max.z));
            var P010 = trans.TransformPoint(new Vector3(min.x, max.y, min.z));
            var P011 = trans.TransformPoint(new Vector3(min.x, max.y, max.z));
            var P100 = trans.TransformPoint(new Vector3(max.x, min.y, min.z));
            var P101 = trans.TransformPoint(new Vector3(max.x, min.y, max.z));
            var P110 = trans.TransformPoint(new Vector3(max.x, max.y, min.z));
            var P111 = trans.TransformPoint(new Vector3(max.x, max.y, max.z));

            return new Vector3[] { P000, P001, P010, P011, P100, P101, P110, P111 };
            /*
            var vertices = new Vector3[8];
            var thisMatrix = col.transform.localToWorldMatrix;
            var storedRotation = col.transform.rotation;
            col.transform.rotation = Quaternion.identity;

            var extents = col.bounds.extents;
            vertices[0] = thisMatrix.MultiplyPoint3x4(-extents);
            vertices[1] = thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, -extents.y, extents.z));
            vertices[2] = thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, extents.y, -extents.z));
            vertices[3] = thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, extents.y, extents.z));
            vertices[4] = thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, -extents.y, -extents.z));
            vertices[5] = thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, -extents.y, extents.z));
            vertices[6] = thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, extents.y, -extents.z));
            vertices[7] = thisMatrix.MultiplyPoint3x4(extents);

            col.transform.rotation = storedRotation;
            return vertices;
            */
        }
    }
}