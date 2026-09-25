#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class RoguelikePrototypeSceneBuilder
{
    [MenuItem("Tools/Roguelike Prototype/Create Prototype Controller")]
    public static void CreatePrototypeController()
    {
        ProceduralDungeonPrototype existing = Object.FindAnyObjectByType<ProceduralDungeonPrototype>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("Prototype controller already exists in the scene.");
            return;
        }

        if (Camera.main == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10f);
        }

        GameObject controller = new GameObject("Procedural Dungeon Prototype Controller");
        controller.AddComponent<ProceduralDungeonPrototype>();
        Selection.activeGameObject = controller;
        EditorUtility.SetDirty(controller);
        Debug.Log("Created Procedural Dungeon Prototype Controller. Press Play to run the prototype.");
    }
}
#endif
