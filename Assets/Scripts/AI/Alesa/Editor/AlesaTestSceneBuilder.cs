using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace Alesa.Editor
{
    /// <summary>
    /// Editor utility to generate the isolated Day 2 test scene for Agent 1 (Alesa).
    /// Creates a hospital-like maze layout with a T-junction, multiple routes,
    /// a blocked corridor area, and actor placeholders.
    /// </summary>
    public static class AlesaTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/AlesaAgentTest.unity";

        [MenuItem("ALESA/Build Agent 1 Test Scene", false, 10)]
        public static void BuildScene()
        {
            // 1. Create a fresh scene with default camera and light
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Ensure Scenes directory exists
            if (!Directory.Exists("Assets/Scenes"))
            {
                Directory.CreateDirectory("Assets/Scenes");
            }

            // 2. Setup Lighting & Camera positioning
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0f, 22f, -14f);
                mainCamera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            }

            // 3. Root Organizers
            var envRoot = new GameObject("--- ENVIRONMENT ---");
            var actorsRoot = new GameObject("--- ACTORS ---");
            var navRoot = new GameObject("--- NAVIGATION ---");

            // 4. Floor (40m x 40m area)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.parent = envRoot.transform;
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f); // 40m x 40m

            // 5. Walls Container
            var wallsRoot = new GameObject("Maze_Walls");
            wallsRoot.transform.parent = envRoot.transform;

            // Outer Perimeter Walls
            CreateWall(wallsRoot, "Wall_North", new Vector3(0f, 1.5f, 18f), new Vector3(36f, 3f, 1f));
            CreateWall(wallsRoot, "Wall_South", new Vector3(0f, 1.5f, -18f), new Vector3(36f, 3f, 1f));
            CreateWall(wallsRoot, "Wall_West", new Vector3(-18f, 1.5f, 0f), new Vector3(1f, 3f, 36f));
            CreateWall(wallsRoot, "Wall_East", new Vector3(18f, 1.5f, 0f), new Vector3(1f, 3f, 36f));

            // Interior Corridor Walls forming a T-Junction maze
            // Center spine separating Left and Right wings
            CreateWall(wallsRoot, "Wall_Center_Spine", new Vector3(0f, 1.5f, 2f), new Vector3(1f, 3f, 12f));

            // Starting corridor side walls (leads north from -16 to -4)
            CreateWall(wallsRoot, "Wall_Start_Left", new Vector3(-3f, 1.5f, -10f), new Vector3(1f, 3f, 12f));
            CreateWall(wallsRoot, "Wall_Start_Right", new Vector3(3f, 1.5f, -10f), new Vector3(1f, 3f, 12f));

            // Left (West) corridor outer boundary
            CreateWall(wallsRoot, "Wall_West_Inner", new Vector3(-9f, 1.5f, 2f), new Vector3(1f, 3f, 12f));

            // Right (East) corridor outer boundary
            CreateWall(wallsRoot, "Wall_East_Inner", new Vector3(9f, 1.5f, 2f), new Vector3(1f, 3f, 12f));

            // North Exit Funnel walls
            CreateWall(wallsRoot, "Wall_North_Funnel_Left", new Vector3(-4f, 1.5f, 11f), new Vector3(7f, 3f, 1f));
            CreateWall(wallsRoot, "Wall_North_Funnel_Right", new Vector3(4f, 1.5f, 11f), new Vector3(7f, 3f, 1f));

            // 6. Blocked Corridor Demonstration Obstacle
            var obstacleRoot = new GameObject("--- HAZARDS & OBSTACLES ---");
            var blockedDebris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blockedDebris.name = "Blocked_Corridor_Obstacle";
            blockedDebris.transform.parent = obstacleRoot.transform;
            blockedDebris.transform.position = new Vector3(-4.5f, 1.5f, 2f);
            blockedDebris.transform.localScale = new Vector3(4f, 3f, 2f);
            // By default inactive, so both routes are initially open for basic tests
            blockedDebris.SetActive(false);

            // 7. Actors (Placeholders)
            // Nathan (Player)
            var nathan = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            nathan.name = "Player_Nathan";
            nathan.transform.parent = actorsRoot.transform;
            nathan.transform.position = new Vector3(0f, 1f, -14f);

            // Alesa (Companion)
            var alesa = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            alesa.name = "Companion_Alesa";
            alesa.transform.parent = actorsRoot.transform;
            alesa.transform.position = new Vector3(0f, 1f, -16f); // trailing behind Nathan

            // Exit Destination
            var exitDest = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            exitDest.name = "Exit_Destination";
            exitDest.transform.parent = envRoot.transform;
            exitDest.transform.position = new Vector3(0f, 0.2f, 15f);
            exitDest.transform.localScale = new Vector3(3f, 0.2f, 3f);

            // 8. Save the scene
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (saved)
            {
                Debug.Log($"<color=green>[ALESA]</color> Successfully built and saved Agent 1 test scene to: {ScenePath}");
                EditorUtility.DisplayDialog("ALESA Agent 1", "Successfully generated test scene:\n" + ScenePath, "OK");
            }
            else
            {
                Debug.LogError($"[ALESA] Failed to save scene to {ScenePath}");
            }
        }

        private static GameObject CreateWall(GameObject parent, string name, Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.parent = parent.transform;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            return wall;
        }
    }
}

