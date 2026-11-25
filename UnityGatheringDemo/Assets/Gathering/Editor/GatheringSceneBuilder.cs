using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GatheringDemo;

public static class GatheringSceneBuilder
{
    [MenuItem("Tools/Gathering/Create Playable Scene")]
    public static void Build()
    {
        if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Stop Play mode before creating the scene");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        if(!AssetDatabase.IsValidFolder("Assets/Gathering/Scenes"))AssetDatabase.CreateFolder("Assets/Gathering","Scenes");
        var settings=AssetDatabase.LoadAssetAtPath<GatheringSettings>("Assets/Gathering/Logistics.asset");
        if(!settings){settings=ScriptableObject.CreateInstance<GatheringSettings>();AssetDatabase.CreateAsset(settings,"Assets/Gathering/Logistics.asset");}
        var root=new GameObject("Gathering Demo");root.AddComponent<GatheringWorld>().SetSettings(settings);root.AddComponent<GatheringController>();
        PlayerSettings.runInBackground=true;
        var player=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if(player.Length>0){var so=new SerializedObject(player[0]);var prop=so.FindProperty("activeInputHandler");if(prop!=null){prop.intValue=1;so.ApplyModifiedPropertiesWithoutUndo();}}
        EditorSceneManager.SaveScene(scene,"Assets/Gathering/Scenes/GatheringDemo.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Gathering/Scenes/GatheringDemo.unity",true)};
        AssetDatabase.SaveAssets();Selection.activeObject=root;Debug.Log("GATHERING_SCENE_READY Assets/Gathering/Scenes/GatheringDemo.unity");
    }
}
