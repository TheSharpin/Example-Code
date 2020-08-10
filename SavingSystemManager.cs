using Gimle.Utils;
using Gimle.Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System;
using System.Linq;
using UnityEngine.SceneManagement;

namespace Gimle.Saving
{
    public class SavingSystemManager : MonoBehaviour
    {   
        [SerializeField] float loadingFadeTime = 1f;

        private const string defaultSaveName = "Save";
        private const string quicksaveName = "Quicksave";
        private const string autosaveName = "Autosave";

        private const string savesCounterFileName = "SavesProfiler.conf";

        private List<Save> allSaves = new List<Save>();
        public List<Save> AllSaves => allSaves;
        public List<Save> SortedSavesByDateTime => allSaves.OrderBy(x => x.saveDate).Reverse().ToList();

        private static int manualSavesCounter; 
        public static int ManualSavesCounter
        {
            get => manualSavesCounter;
            set
            {
                manualSavesCounter = value;

                //Save counter to separate file than saves
                string path = Path.Combine(Application.persistentDataPath, savesCounterFileName);
                using (FileStream stream = File.Open(path, FileMode.OpenOrCreate))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(stream, manualSavesCounter);
                }
            }
        }

        private void Start()
        {
            LoadManualSavesCounter();
            ConvertSaveFiles();
        }

        private void LoadManualSavesCounter()
        {
            string path = Path.Combine(Application.persistentDataPath, savesCounterFileName);
            if (!File.Exists(path)) return;

            using (FileStream stream = File.Open(path, FileMode.Open))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                manualSavesCounter = (int)formatter.Deserialize(stream);
            }
        }

        /// <summary>
        /// Convert save files to classes' objects
        /// </summary>
        private void ConvertSaveFiles()
        {
            allSaves = new List<Save>();

            DirectoryInfo saveDir = new DirectoryInfo(Application.persistentDataPath);
            FileInfo[] saveFiles = saveDir.GetFiles("*.sav");
            bool createdNewSave;

            for (int i = 0; i < saveFiles.Length; i++)
            {
                allSaves.Add(DeserializeSaveFile(Path.GetFileNameWithoutExtension(saveFiles[i].Name), out createdNewSave));
            }
        }

        /// <summary>
        /// Saves new files and overrides existing save files.
        /// </summary>
        /// <param name="saveFile"></param>
        public void Save(string saveFile)
        {
            bool createdNewSave;
            Save save = DeserializeSaveFile(saveFile, out createdNewSave);
            int sceneIndex = SceneManager.GetActiveScene().buildIndex;
            SaveStates(save.SavedScenesDataLookup[sceneIndex]);
            save.lastSceneIndex = sceneIndex;
            save.saveDate = DateTime.Now.ToLocalTime();
            SerializeSaveFile(save.saveName, save);

            // If deseralization created new empty save then add it to lookup
            if(createdNewSave)
                allSaves.Add(save);
            // Otherwise deserialization overrided existeing save
            else
            {
                for (int i = 0; i < allSaves.Count; i++)
                {
                    if (allSaves[i].saveName == save.saveName)
                    {
                        allSaves[i] = save;
                        break;
                    }
                }
            }

            Debug.Log("SAVE SCENE INDEX: " + save.lastSceneIndex);
        }

        public void Load(string saveFile)
        {
            StartCoroutine(LoadRoutine(saveFile));
        }

        private IEnumerator LoadRoutine(string saveFile)
        {
            GameManager.Instance.SetInput(false);

            bool createdNewSave;
            Save save = DeserializeSaveFile(saveFile, out createdNewSave);
            Debug.Log("LOAD SCENE INDEX: " + save.lastSceneIndex);
            Action[] actions = { () => LoadStates(save.SavedScenesDataLookup[save.lastSceneIndex]) };
            yield return SceneManagement.ScenesManager.Instance.LoadTransition(actions, save.lastSceneIndex);

            GameManager.Instance.SetInput(true);
        }


        public void DeleteSave(string saveFile)
        {
            string path = GetPathFromSaveFile(saveFile);

            if (File.Exists(path))
            {
                File.Delete(path);

                Save save = allSaves.FirstOrDefault(x => x.saveName == saveFile);

                if (save != null)
                    allSaves.Remove(save);
                // If save cannot be found in allSaves list then after removing save file convert files to list again
                else
                    ConvertSaveFiles();
            }
        }


        #region Serialization
        private void SerializeSaveFile(string saveFile, Save save)
        {
            string path = GetPathFromSaveFile(saveFile);
            using (FileStream stream = File.Open(path,FileMode.Create))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, save);
            }
        }

        private Save DeserializeSaveFile(string saveFile, out bool createdNewSave)
        {
            string path = GetPathFromSaveFile(saveFile);

            if (!File.Exists(path))
            {
                createdNewSave = true;
                return new Save(saveFile);
            }

            using (FileStream stream = File.Open(path, FileMode.Open))
            {
                createdNewSave = false;
                BinaryFormatter formatter = new BinaryFormatter();
                return (Save)formatter.Deserialize(stream);
            }
        }
        #endregion

        /// <summary>
        /// Adds to a sended Save as param states from saveable entities in current scene
        /// </summary>
        /// <returns></returns>
        private void SaveStates(Dictionary<string, object> states)
        {
            foreach (SaveableEntity entity in FindObjectsOfType<SaveableEntity>())
            {
                states[entity.GetUUID()] = entity.SaveState();
            }
        }

        /// <summary>
        /// Load state for every saveable entity which UUID is in dicionary parameter as string
        /// </summary>
        /// <param name="states"></param>
        private void LoadStates(Dictionary<string, object> states)
        {
            foreach (SaveableEntity entity in FindObjectsOfType<SaveableEntity>())
            {
                string id = entity.GetUUID();

                if (states.ContainsKey(id))
                {
                    entity.LoadState(states[id]);
                }
            }
        }

        #region Getters

        public static string DefaultSaveName => defaultSaveName;
        public static string QuicksaveName => quicksaveName;
        public static string AutosaveName => autosaveName;

        public bool FileExists(string saveFile)
        {
            return File.Exists(GetPathFromSaveFile(saveFile));
        }

        private string GetPathFromSaveFile(string saveFile)
        {
            return Path.Combine(Application.persistentDataPath, saveFile + ".save");
        }
        #endregion
    }

    [Serializable]
    public class Save
    {
        public string saveName;
        public DateTime saveDate;
        public int lastSceneIndex;
        public string locationName;
        public int playerLevel;
        public Dictionary<int, Dictionary<string, object>>SavedScenesDataLookup;

        public Save(string Name)
        {
            saveName = Name;
            saveDate = DateTime.Now.ToLocalTime();
            lastSceneIndex = SceneManager.GetActiveScene().buildIndex;
            locationName = SceneManager.GetActiveScene().name;
            playerLevel = GameManager.Instance.playerLevel;

            SavedScenesDataLookup = new Dictionary<int, Dictionary<string, object>>();

            //Create dictionary with all scenes and thier empty states
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                SavedScenesDataLookup.Add(SceneManager.GetSceneAt(i).buildIndex, new Dictionary<string, object>());
            }
        }
    }
}