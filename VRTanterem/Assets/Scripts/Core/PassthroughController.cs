using UnityEngine;
using UnityEngine.InputSystem;

public class PassthroughController : MonoBehaviour
{
    [Header("Oculus Components")]
    [SerializeField] private OVRManager ovrManager; // Ezt valójában nem használjuk a kódban, de jó, ha itt van referenciaként
    [SerializeField] private OVRPassthroughLayer passthroughLayer; // Ezt kapcsolgatjuk
    [SerializeField] private Camera mainCamera; // Ennek a hátterét módosítjuk

    [Header("Scene Objects")]
    [SerializeField] private GameObject virtualEnvironmentRoot; // Ezt kapcsolgatjuk

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset inputActions; // Az asset, ami tartalmazza az action-t
    [SerializeField] private string actionMapName = "SystemControls"; // Az Action Map neve az assetben
    [SerializeField] private string toggleActionName = "TogglePassthrough"; // Az Action neve az Action Map-en belül

    // Input Action referencia
    private InputAction togglePassthroughAction;

    // Eredeti kamera beállítások tárolása
    private CameraClearFlags originalClearFlags;
    private Color originalBackgroundColor;

    // Aktuális állapot követése
    private bool isPassthroughActive = false;

    // --- Életciklus Metódusok ---

    void Awake()
    {
        // Logolás a legelején
        Debug.Log($"--- PASSTHROUGH AWAKE --- Time: {Time.time} --- GameObject: {gameObject.name}, Active: {gameObject.activeInHierarchy}, Component Enabled: {this.enabled}");

        // --- Referenciák Ellenőrzése (KRITIKUS) ---
        // Ellenőrizzük a vizuális elemekhez szükséges referenciákat, mielőtt bármit csinálnánk velük
        if (passthroughLayer == null)
        {
            Debug.LogError("!!! AWAKE ERROR: OVRPassthroughLayer reference (passthroughLayer) is NULL! Assign it in the Inspector.", this);
            enabled = false; // Letiltjuk a komponenst, mert nem tudna működni
            return;
        }
        if (mainCamera == null)
        {
            Debug.LogError("!!! AWAKE ERROR: Main Camera reference (mainCamera) is NULL! Assign it in the Inspector.", this);
            enabled = false;
            return;
        }
        if (virtualEnvironmentRoot == null)
        {
            Debug.LogError("!!! AWAKE ERROR: Virtual Environment Root reference (virtualEnvironmentRoot) is NULL! Assign it in the Inspector.", this);
            enabled = false;
            return;
        }
        Debug.Log("   Awake: Visual component references seem OK.");

        // --- Eredeti Kamera Beállítások Mentése ---
        // Mentsük el az indulási állapotot, MIELŐTT esetleg módosítanánk rajta
        originalClearFlags = mainCamera.clearFlags;
        originalBackgroundColor = mainCamera.backgroundColor;
        Debug.Log($"   Awake: Original camera settings saved: ClearFlags={originalClearFlags}, BackgroundColor={originalBackgroundColor}");

        // --- Input Action Keresése ---
        if (inputActions == null)
        {
            Debug.LogError("!!! AWAKE ERROR: InputActions asset is NULL! Assign it in the Inspector.", this);
            enabled = false;
            return;
        }
        var actionMap = inputActions.FindActionMap(actionMapName);
        if (actionMap == null)
        {
            Debug.LogError($"!!! AWAKE ERROR: Action Map '{actionMapName}' NOT FOUND in the assigned InputActions asset!", this);
            enabled = false;
            return;
        }
        togglePassthroughAction = actionMap.FindAction(toggleActionName);
        if (togglePassthroughAction == null)
        {
            Debug.LogError($"!!! AWAKE ERROR: Action '{toggleActionName}' NOT FOUND in Action Map '{actionMapName}'!", this);
            enabled = false;
            return;
        }
        Debug.Log($"--- AWAKE SUCCESS: Action '{toggleActionName}' FOUND. Ready for OnEnable.");
    }

    void Start()
    {
        // Logolás
        Debug.Log($"--- PASSTHROUGH START --- Time: {Time.time} ---");

        // --- Kezdeti Állapot Beállítása ---
        // Biztosítjuk, hogy az alkalmazás VR módban induljon (a mentett eredeti beállításokkal)
        // Ezt Start()-ban hívjuk, hogy az Awake-ben biztosan minden beállítás megtörténjen.
        // A SetPassthroughState(false) gondoskodik a kamera, layer és environment helyes beállításáról.
        Debug.Log("   Start: Setting initial state to VR mode (Passthrough Disabled)...");
        SetPassthroughState(false); // Explicit beállítjuk a VR módot induláskor
    }

    private void OnEnable()
    {
        // Logolás a legelején
        Debug.Log($"--- PASSTHROUGH ONENABLE --- Time: {Time.time} --- GameObject: {gameObject.name}, Active: {gameObject.activeInHierarchy}, Component Enabled: {this.enabled}");

        // Listener hozzáadása és engedélyezés
        if (togglePassthroughAction != null)
        {
            Debug.Log($"   OnEnable: Attaching listener and enabling action '{toggleActionName}'...");
            togglePassthroughAction.performed += OnTogglePassthroughPerformed;
            togglePassthroughAction.Enable();
            // Ellenőrizzük, hogy tényleg engedélyezve lett-e
            Debug.Log($"   OnEnable: Listener attached. Action '{toggleActionName}' enabled state: {togglePassthroughAction.enabled}");
        }
        else
        {
            // Ha ide jut, akkor Awake-ben hiba volt. Az Awake log már jelzi ezt.
            Debug.LogError($"!!! ONENABLE ERROR: Cannot enable action because togglePassthroughAction is NULL (check Awake logs)!");
        }
    }

    private void OnDisable()
    {
        // Logolás a legelején
        Debug.Log($"--- PASSTHROUGH ONDISABLE --- Time: {Time.time} --- GameObject: {gameObject.name}, Active: {gameObject.activeInHierarchy}, Component Enabled: {this.enabled}");

        // Listener eltávolítása és letiltás
        if (togglePassthroughAction != null)
        {
            Debug.Log($"   OnDisable: Detaching listener and potentially disabling action '{toggleActionName}'...");
            togglePassthroughAction.performed -= OnTogglePassthroughPerformed;

            // Csak akkor próbáljuk letiltani, ha engedélyezve volt
            if (togglePassthroughAction.enabled)
            {
                togglePassthroughAction.Disable();
                Debug.Log($"   OnDisable: Listener detached and action disabled.");
            }
            else
            {
                Debug.LogWarning($"   OnDisable: Listener detached, but action was already disabled or never enabled properly.");
            }
        }
        else
        {
            // Ha ide jut, akkor Awake-ben hiba volt. Az Awake log már jelzi ezt.
            Debug.LogError($"!!! ONDISABLE ERROR: Cannot disable action because togglePassthroughAction is NULL (check Awake logs)!");
        }
    }

    public void TogglePassthrough()
    {
        // Logoljuk, hogy ez a specifikus metódus lett meghívva, és mi az aktuális állapot
        Debug.Log($"--- TogglePassthrough() method called (e.g., from UI Button). Current isPassthroughActive: {isPassthroughActive} ---");

        // Ugyanazt a logikát használjuk, mint az InputAction eseménykezelő:
        // megfordítjuk az aktuális állapotot.
        SetPassthroughState(!isPassthroughActive);
    }

    // --- Input Esemény Kezelő ---

    private void OnTogglePassthroughPerformed(InputAction.CallbackContext context)
    {
        // Ez az a log, amit már láttál működni!
        Debug.Log("!!!!!!!!!! TOGGLE PASSTHROUGH ACTION PERFORMED !!!!!!!!!!");

        // Itt hívjuk meg a váltást végző logikát
        Debug.Log($"   Action '{toggleActionName}' performed. Calling SetPassthroughState to toggle.");
        SetPassthroughState(!isPassthroughActive); // Megfordítjuk az aktuális állapotot
    }

    // --- Vizuális Váltás Logikája ---

    // Ezt a metódust hívja az OnTogglePassthroughPerformed és a Start
    public void SetPassthroughState(bool enablePassthrough)
    {
        // Logoljuk a hívást és az állapotokat
        Debug.Log($"===== SetPassthroughState called: Requesting enablePassthrough = {enablePassthrough}, Current state (isPassthroughActive) = {isPassthroughActive} =====");

        // Ellenőrizzük, hogy tényleg kell-e váltani
        if (enablePassthrough == isPassthroughActive)
        {
            Debug.LogWarning("   SetPassthroughState: Requested state is the same as current state. No change needed.");
            return; // Nincs változás
        }

        // --- Referenciák Ellenőrzése (Biztonsági okokból itt is) ---
        // Bár Awake-ben ellenőriztük, egy extra check itt nem árt.
        if (passthroughLayer == null || mainCamera == null || virtualEnvironmentRoot == null)
        {
            Debug.LogError("!!! SetPassthroughState CRITICAL ERROR: One or more required component references are NULL! Cannot perform visual switch. Check Inspector and Awake logs!");
            // Nem állítjuk át az isPassthroughActive-ot, mert a váltás nem tudott megtörténni!
            return;
        }

        // Átállítjuk a belső állapotváltozót
        isPassthroughActive = enablePassthrough;
        Debug.Log($"   Internal state (isPassthroughActive) updated to: {isPassthroughActive}");

        // 1. OVRPassthroughLayer komponens engedélyezése/letiltása
        passthroughLayer.enabled = enablePassthrough;
        Debug.Log($"   OVRPassthroughLayer component '.enabled' set to: {passthroughLayer.enabled}");

        // 2. Kamera hátterének beállítása (URP kompatibilis módon)
        if (enablePassthrough)
        {
            // Passthrough Mód: Solid Color, átlátszó háttérrel
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.clear; // RGBA(0,0,0,0)
            Debug.Log($"   Camera background set for Passthrough: ClearFlags={mainCamera.clearFlags}, BackgroundColor={mainCamera.backgroundColor}");
        }
        else
        {
            // Virtuális Mód: Eredeti beállítások visszaállítása
            mainCamera.clearFlags = originalClearFlags; // Visszaállítás az Awake-ben mentett értékre
            mainCamera.backgroundColor = originalBackgroundColor; // Visszaállítás az Awake-ben mentett értékre
            Debug.Log($"   Camera background restored for VR: ClearFlags={mainCamera.clearFlags}, BackgroundColor={mainCamera.backgroundColor}");
        }

        // 3. Virtuális környezet GameObject aktiválása/deaktiválása
        virtualEnvironmentRoot.SetActive(!enablePassthrough); // Ha passthrough aktív, a környezet inaktív, és fordítva.
        Debug.Log($"   VirtualEnvironmentRoot GameObject '.SetActive()' called with: {!enablePassthrough}");

        Debug.Log($"===== SetPassthroughState finished successfully for state: {enablePassthrough} =====");
    }
}
