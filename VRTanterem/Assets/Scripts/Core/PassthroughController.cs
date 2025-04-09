using UnityEngine;
using Oculus.Interaction; // Szükség lehet rá, de lehet elég a sima UnityEngine is
using UnityEngine.InputSystem;

public class PassthroughController : MonoBehaviour
{
    [Header("Oculus Components")]
    [SerializeField] private OVRManager ovrManager;
    [SerializeField] private OVRPassthroughLayer passthroughLayer; // Ajánlott, de lehet null is
    [SerializeField] private Camera mainCamera; // A CenterEyeAnchor alatti kamera

    [Header("Scene Objects")]
    [SerializeField] private GameObject virtualEnvironmentRoot; // A tanterem gyökér GameObject-je
    // [SerializeField] private GameObject passthroughSpecificUI; // Ha lenne ilyen

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset inputActions; // Húzd ide ugyanazt az assetet, amit a WhisperMicController is használ
    [SerializeField] private string actionMapName = "SystemControls"; // Vagy amilyen Action Map-et használtál az 1. lépésben
    [SerializeField] private string toggleActionName = "TogglePassthrough"; // Az Action neve, amit az 1. lépésben létrehoztál

    private InputAction togglePassthroughAction;

    // Eredeti kamera beállítások tárolása
    private CameraClearFlags originalClearFlags;
    private Color originalBackgroundColor;
    private bool originalPassthroughLayerState;

    // Aktuális állapot követése
    private bool isPassthroughActive = false;

    void Start()
    {
        // Referenciák ellenőrzése
        if (ovrManager == null || mainCamera == null || virtualEnvironmentRoot == null)
        {
            Debug.LogError("PassthroughController: Hiányzó referenciák! Kérlek, állítsd be őket az Inspectorban.", this);
            enabled = false;
            return;
        }

        // Eredeti kamera beállítások mentése (feltételezzük, hogy virtuális módban indulunk)
        originalClearFlags = mainCamera.clearFlags;
        originalBackgroundColor = mainCamera.backgroundColor;
        if (passthroughLayer != null)
        {
            originalPassthroughLayerState = passthroughLayer.enabled; // Mentsük el a layer kezdeti állapotát is
        }

        // --- ÚJ: Input Action Keresése ---
        var actionMap = inputActions.FindActionMap(actionMapName);
        if (actionMap == null)
        {
            Debug.LogError($"Action Map '{actionMapName}' not found in the provided InputActionAsset!", this);
            enabled = false;
            return;
        }
        togglePassthroughAction = actionMap.FindAction(toggleActionName);
        if (togglePassthroughAction == null)
        {
            Debug.LogError($"Action '{toggleActionName}' not found in Action Map '{actionMapName}'!", this);
            enabled = false;
            return;
        }

        // VR módban indulás
        SetPassthroughState(false);
    }

    // --- ÚJ: OnEnable és OnDisable ---
    private void OnEnable()
    {
        if (togglePassthroughAction != null)
        {
            // Feliratkozás a 'performed' eseményre. Ez akkor sül el, amikor a gombnyomás befejeződik.
            togglePassthroughAction.performed += OnTogglePassthroughPerformed;
            togglePassthroughAction.Enable(); // Engedélyezzük az Action figyelését
            Debug.Log($"'{toggleActionName}' action enabled and listener attached.");
        }
    }

    private void OnDisable()
    {
        if (togglePassthroughAction != null)
        {
            togglePassthroughAction.performed -= OnTogglePassthroughPerformed;
            togglePassthroughAction.Disable(); // Letiltjuk az Action figyelését
            Debug.Log($"'{toggleActionName}' action disabled and listener detached.");
        }
    }

    private void OnTogglePassthroughPerformed(InputAction.CallbackContext context)
    {
        Debug.LogError("!!!!!!!!!! TOGGLE PASSTHROUGH ACTION PERFORMED !!!!!!!!!!"); // MARADJON BENT A DEBUG!
        Debug.Log($"'{toggleActionName}' action performed. Toggling Passthrough.");
        // Itt hívjuk a publikus metódust, ami logol és kezeli a dupla hívást
        SetPassthroughState(!isPassthroughActive);
    }

    // Ezt a metódust hívhatod meg egy gombnyomásra vagy más eseményre
    public void TogglePassthrough()
    {
        SetPassthroughState(!isPassthroughActive);
    }

    // A tényleges váltást végző metódus
    public void SetPassthroughState(bool enablePassthrough)
    {
        if (enablePassthrough == isPassthroughActive) return; // Nincs változás

        isPassthroughActive = enablePassthrough;

        // 1. OVRPassthroughLayer komponens vezérlése (EZ LESZ A FŐ KAPCSOLÓ)
        if (passthroughLayer != null)
        {
            // Egyszerűen engedélyezzük vagy letiltjuk magát a komponenst
            passthroughLayer.enabled = enablePassthrough;
            Debug.Log($"OVRPassthroughLayer component enabled: {enablePassthrough}");
        }
        else
        {
            // Ha nincs OVRPassthroughLayer komponens, ez a módszer nem fog működni.
            // Győződj meg róla, hogy hozzáadtad az OVRCameraRig-hez és beállítottad a referenciát.
            Debug.LogError("OVRPassthroughLayer reference is null or component missing! Cannot toggle Passthrough programmatically this way.", this);
            // Ebben az esetben nem tudjuk biztonságosan váltani az állapotot, így visszalépünk.
            isPassthroughActive = !enablePassthrough; // Visszaállítjuk az állapotváltozót
            return;
        }

        // 2. Kamera hátterének beállítása (Ez továbbra is szükséges)
        if (enablePassthrough)
        {
            // Passthrough Mód: Átlátszó háttér
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.clear; // RGBA(0,0,0,0)
        }
        else
        {
            // Virtuális Mód: Eredeti beállítások visszaállítása
            mainCamera.clearFlags = originalClearFlags;
            mainCamera.backgroundColor = originalBackgroundColor;
        }

        // 3. Virtuális környezet ki/bekapcsolása (Ez is marad)
        virtualEnvironmentRoot.SetActive(!enablePassthrough);

        // 4. (Opcionális) Csak Passthrough-ban látható elemek kezelése
        // if (passthroughSpecificUI != null)
        // {
        //     passthroughSpecificUI.SetActive(enablePassthrough);
        // }

        Debug.Log($"Passthrough state set to: {enablePassthrough}");
    }

}
