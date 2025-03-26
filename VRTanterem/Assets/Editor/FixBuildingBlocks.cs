using UnityEngine;

using UnityEditor;



public class FixBuildingBlocks : EditorWindow

{

    [MenuItem("Tools/Fix Building Blocks")]

    public static void FixBlocks()

    {

        EditorPrefs.DeleteKey(null);

        Debug.Log("Null key deleted. Restart Unity to see Building Blocks.");

    }

}