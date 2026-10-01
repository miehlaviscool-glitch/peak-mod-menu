using System;
using System.IO;

namespace Doorstop
{
    // Unity Doorstop 4.x calls Doorstop.Entrypoint.Start() in the target assembly.
    // This runs before Unity has registered its native bindings, so touch NO Unity API here.
    public static class Entrypoint
    {
        static bool created;

        public static void Start()
        {
            Log("Entrypoint.Start reached");
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (created) return;
            if (args.LoadedAssembly.GetName().Name != "Assembly-CSharp") return;
            created = true;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            Log("Assembly-CSharp loaded");
            HookScene();
        }

        // Managed-only event: no native call, and fires only once the first scene is up.
        static void HookScene()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static bool made;

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (made) return;
            made = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            Log("First scene loaded: " + scene.name);
            Create();
        }

        static void Create()
        {
            try
            {
                var go = new UnityEngine.GameObject("PeakMenu");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                go.AddComponent<PeakMenu.Menu>();
                Log("Menu created");
            }
            catch (Exception e)
            {
                Log(e.ToString());
            }
        }

        public static void Log(string s)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeakMenu.log"), DateTime.Now + " " + s + "\n"); }
            catch { }
        }
    }
}
