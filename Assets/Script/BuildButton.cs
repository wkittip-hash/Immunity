using UnityEngine;
using UnityEngine.UI;

// Put this on a UI Button and drag a building prefab in. Clicking the button starts placing that building.
// (Does the same as wiring the Button's On Click () to BuildingPlacer.BeginPlacement, without the manual setup.)
[RequireComponent(typeof(Button))]
public class BuildButton : MonoBehaviour
{
    [Tooltip("Building prefab to place, e.g. SwarmSpawn.")]
    [SerializeField] CapillaryPort buildingPrefab;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        if (BuildingPlacer.Instance == null)
        {
            Debug.LogError("BuildButton: no BuildingPlacer in the scene. Add it to the Main Camera.", this);
            return;
        }
        if (buildingPrefab == null)
        {
            Debug.LogError(name + ": no Building Prefab assigned on BuildButton.", this);
            return;
        }
        BuildingPlacer.Instance.BeginPlacement(buildingPrefab);
    }
}
