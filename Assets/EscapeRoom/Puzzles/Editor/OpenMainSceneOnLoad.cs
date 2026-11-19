using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeRoom.Editor
{
    /// <summary>
    /// Makes the escape room the scene you work in:
    ///  - once per editor session, if an empty / untitled / sample scene is open, opens EscapeRoom.unity;
    ///  - if EscapeRoom.unity does not contain the in-room Puzzles 2 + 3 yet, places them (and saves).
    /// Menu: Escape Room > Open Main Scene (Room) does the same on demand.
    /// </summary>
    [InitializeOnLoad]
    static class OpenMainSceneOnLoad
    {
        const string DoneKey = "EscapeRoom.OpenMainSceneOnLoad.Done";

        static OpenMainSceneOnLoad()
        {
            if (Application.isBatchMode || SessionState.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Run;
                return;
            }
            SessionState.SetBool(DoneKey, true);
            var active = SceneManager.GetActiveScene();
            bool isMain = active.path == CooperativePuzzleBuilder.MainScene;
            bool emptyOrSample = string.IsNullOrEmpty(active.path) || active.path == "Assets/Scenes/SampleScene.unity";
            if (!isMain && !emptyOrSample) return; // the user deliberately opened another scene
            OpenMain();
        }

        [MenuItem("Escape Room/Open Main Scene (Room)", priority = 0)]
        public static void OpenMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != CooperativePuzzleBuilder.MainScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(CooperativePuzzleBuilder.MainScene, OpenSceneMode.Single);
            }
            if (Object.FindAnyObjectByType<RoomPuzzleStations>(FindObjectsInactive.Include) == null)
                RoomPuzzleInstaller.Place();
            Debug.Log("[EscapeRoom] Main scene (rooms + Puzzles 1-3) is open. Press Play.");
        }
    }
}
