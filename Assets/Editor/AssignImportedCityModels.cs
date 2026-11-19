using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Relie les bâtiments importés et le gestionnaire de livraison à la scène active.</summary>
[InitializeOnLoad]
static class AssignImportedCityModels
{
    static bool finished;
    static AssignImportedCityModels() { EditorApplication.update += TryAssign; }

    static void TryAssign()
    {
        if (finished || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        RoadsideExperience city = Object.FindFirstObjectByType<RoadsideExperience>();
        if (city == null) return;

        bool changed = false;
        if (city.cityBuildingModels == null || city.cityBuildingModels.Length != 3 ||
            city.cityBuildingModels[0] == null || city.cityBuildingModels[1] == null || city.cityBuildingModels[2] == null)
        {
            GameObject[] models = {
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Exports/FBX (Unity)/Building_Small_1.fbx"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Exports/FBX (Unity)/Building_Medium_2_001.fbx"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Exports/FBX (Unity)/Building_Large_2.fbx")
            };
            if (models[0] != null && models[1] != null && models[2] != null)
            {
                city.cityBuildingModels = models;
                changed = true;
            }
        }

        Transform car = city.car != null ? city.car : (city.route != null ? city.route.car : null);
        if (car == null)
        {
            CarController found = Object.FindFirstObjectByType<CarController>();
            if (found != null) car = found.transform;
        }
        if (car != null)
        {
            TruckGameplay gameplay = car.GetComponent<TruckGameplay>();
            if (gameplay == null) { gameplay = car.gameObject.AddComponent<TruckGameplay>(); changed = true; }
            if (gameplay.route != city.route) { gameplay.route = city.route; changed = true; }
            if (gameplay.police != city) { gameplay.police = city; changed = true; }
            if (gameplay.controller != car.GetComponent<CarController>()) { gameplay.controller = car.GetComponent<CarController>(); changed = true; }
        }

        if (changed)
        {
            EditorUtility.SetDirty(city);
            if (car != null) EditorUtility.SetDirty(car.gameObject);
            EditorSceneManager.MarkSceneDirty(city.gameObject.scene);
            EditorSceneManager.SaveScene(city.gameObject.scene);
            Debug.Log("Smuggler Run : livraisons, dégâts et améliorations ajoutés à la scène.");
        }
        finished = true;
        EditorApplication.update -= TryAssign;
    }
}
