using UnityEngine;
using UnityEngine.InputSystem;

public class PassthroughController : MonoBehaviour
{
    [Header("Oculus Components")]
    [SerializeField] private OVRManager ovrManager;
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Camera mainCamera;

    [Header("Scene Objects")]
    [SerializeField] private GameObject virtualEnvironmentRoot;

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string actionMapName = "SystemControls";
    [SerializeField] private string toggleActionName = "TogglePassthrough";

    [Header("Teleportation Settings")]
    [SerializeField] private GameObject ovrPlayerObject;
    [SerializeField] private Transform playerPositionTarget;
    [SerializeField] private Transform passthroughPositionTarget;

    [Header("Fade Effect")]
    [SerializeField] private OVRScreenFade ovrScreenFade;
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("UI Element Positioning")]
    [SerializeField] private GameObject uiElementToMove;
    [SerializeField] private Transform uiTargetInPassthrough;

    private Vector3 uiOriginalPosition;
    private Quaternion uiOriginalRotation;
    private bool uiOriginalTransformWasStored = false;

    // Átmenet állapotát követő változó
    private bool isTransitioning = false;

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

        // --- Ellenőrzése a Teleportációhoz és Fade-hez ---
        if (ovrPlayerObject == null)
        {
            Debug.LogError("!!! AWAKE ERROR: OVR Player Object reference (ovrPlayerObject) is NULL! Assign it in the Inspector.", this);
            enabled = false;
            return;
        }
        if (playerPositionTarget == null)
        {
            Debug.LogError("!!! AWAKE ERROR: Player Position Target reference (playerPositionTarget) is NULL! Assign it in the Inspector.", this);
            enabled = false;
            return;
        }
        if (passthroughPositionTarget == null)
        {
            Debug.LogError("!!! AWAKE ERROR: Passthrough Position Target reference (passthroughPositionTarget) is NULL! Assign it in the Inspector.", this);
            enabled = false;
            return;
        }
        if (ovrScreenFade == null)
        {
            if (fadeDuration > 0)
            {
                Debug.LogError("!!! AWAKE ERROR: OVRScreenFade reference (ovrScreenFade) is NULL, but fadeDuration > 0! Assign it in the Inspector or set fadeDuration to 0.", this);
                enabled = false;
                return;
            }
            else
            {
                Debug.LogWarning("  Awake: OVRScreenFade reference is NULL. Fade effect will be skipped.");
            }
        }
        Debug.Log("    Awake: Teleportation and Fade component references seem OK.");

        // --- Referenciák Ellenőrzése a UI Mozgatásához ---
        if (uiElementToMove == null)
        {
            Debug.LogWarning("  Awake: UI Element to Move (uiElementToMove) is NULL. UI positioning will be skipped.", this);
        }
        if (uiTargetInPassthrough == null && uiElementToMove != null)
        {
            Debug.LogWarning("  Awake: UI Target In Passthrough (uiTargetInPassthrough) is NULL. UI cannot be positioned for passthrough.", this);
        }
        Debug.Log("    Awake: Teleportation, Fade, and UI component references checked.");

        // --- Eredeti Kamera Beállítások Mentése ---
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
        Debug.Log($"===== SetPassthroughState called: Requesting enablePassthrough = {enablePassthrough}, Current state (isPassthroughActive) = {isPassthroughActive} =====");

        if (isTransitioning)
        {
            Debug.LogWarning("  SetPassthroughState: Transition already in progress. Ignoring request.");
            return;
        }

        if (enablePassthrough == isPassthroughActive)
        {
            Debug.LogWarning("  SetPassthroughState: Requested state is the same as current state. No change needed.");
            return;
        }

        // Kritikus referenciák ellenőrzése (beleértve az újakat is)
        if (passthroughLayer == null || mainCamera == null || virtualEnvironmentRoot == null ||
            ovrPlayerObject == null || playerPositionTarget == null || passthroughPositionTarget == null)
        {
            Debug.LogError("!!! SetPassthroughState CRITICAL ERROR: One or more required component references for passthrough/teleport are NULL! Cannot perform transition. Check Inspector and Awake logs!");
            return;
        }
        if (ovrScreenFade == null && fadeDuration > 0)
        {
            Debug.LogError("!!! SetPassthroughState CRITICAL ERROR: OVRScreenFade is NULL but fadeDuration > 0. Cannot perform fade. Assign OVRScreenFade or set fadeDuration to 0.");
            // Dönthetsz úgy, hogy fade nélkül folytatod, vagy itt megállsz. Most megállunk.
            return;
        }


        StartCoroutine(PerformTransition(enablePassthrough));
    }

    private System.Collections.IEnumerator PerformTransition(bool enablePassthrough)
    {
        isTransitioning = true;
        Debug.Log($"--- Starting Transition. Target Passthrough State: {enablePassthrough} --- Time: {Time.time}");

        // 1. Fade Out
        if (ovrScreenFade != null && fadeDuration > 0)
        {
            Debug.Log("  Transition: Fading out...");
            ovrScreenFade.fadeTime = fadeDuration;
            ovrScreenFade.FadeOut();
            yield return new WaitForSeconds(fadeDuration);
            Debug.Log("  Transition: Fade out complete.");
        }
        else
        {
            Debug.LogWarning("  Transition: OVRScreenFade not assigned or fadeDuration is 0. Skipping fade out.");
        }

        // 2. Teleportálás
        Debug.Log("  Transition: Performing teleportation...");
        if (enablePassthrough)
        {
            if (passthroughPositionTarget != null)
            {
                ovrPlayerObject.transform.position = passthroughPositionTarget.position;
                ovrPlayerObject.transform.rotation = passthroughPositionTarget.rotation;
                Debug.Log($"    Player ('{ovrPlayerObject.name}') teleported to Passthrough Target: '{passthroughPositionTarget.name}' (Pos: {passthroughPositionTarget.position}, Rot: {passthroughPositionTarget.eulerAngles})");
            }
            else Debug.LogError("  Transition ERROR: passthroughPositionTarget is NULL!");
        }
        else
        {
            if (playerPositionTarget != null)
            {
                ovrPlayerObject.transform.position = playerPositionTarget.position;
                ovrPlayerObject.transform.rotation = playerPositionTarget.rotation;
                Debug.Log($"    Player ('{ovrPlayerObject.name}') teleported to Player (VR) Target: '{playerPositionTarget.name}' (Pos: {playerPositionTarget.position}, Rot: {playerPositionTarget.eulerAngles})");
            }
            else Debug.LogError("  Transition ERROR: playerPositionTarget is NULL!");
        }

        Debug.Log("  Transition: Performing UI element positioning...");
        if (uiElementToMove != null)
        {
            if (enablePassthrough) // Passthrough-ba lépés
            {
                if (uiTargetInPassthrough != null)
                {
                    // Eredeti UI transzformáció elmentése, ha még nem történt meg,
                    // vagy ha minden alkalommal az aktuális VR pozíciót akarjuk menteni.
                    // Most úgy implementálom, hogy az első VR állapotot menti.
                    // Ha azt szeretnéd, hogy mindig az aktuális "passthrough előtti" állapotot mentse,
                    // akkor a uiOriginalTransformWasStored ellenőrzés nélkül mindig mentsd el.
                    if (!uiOriginalTransformWasStored)
                    {
                        uiOriginalPosition = uiElementToMove.transform.position;
                        uiOriginalRotation = uiElementToMove.transform.rotation;
                        uiOriginalTransformWasStored = true;
                        Debug.Log($"    Stored original transform for UI element '{uiElementToMove.name}'.");
                    }

                    uiElementToMove.transform.position = uiTargetInPassthrough.position;
                    uiElementToMove.transform.rotation = uiTargetInPassthrough.rotation;
                    Debug.Log($"    UI Element ('{uiElementToMove.name}') moved to Passthrough Target: '{uiTargetInPassthrough.name}'.");
                }
                else
                {
                    Debug.LogWarning("  Transition: uiTargetInPassthrough is NULL. Cannot position UI element for passthrough mode.");
                }
            }
            else // Passthrough-ból kilépés (vissza VR-be)
            {
                if (uiOriginalTransformWasStored)
                {
                    uiElementToMove.transform.position = uiOriginalPosition;
                    uiElementToMove.transform.rotation = uiOriginalRotation;
                    Debug.Log($"    UI Element ('{uiElementToMove.name}') restored to its original VR position.");
                    // Opcionális: ha azt akarod, hogy a következő passthroughba lépéskor újra elmentse az akkor aktuális pozíciót:
                    // uiOriginalTransformWasStored = false;
                }
                else
                {
                    Debug.LogWarning("  Transition: Original UI transform was not stored. Cannot restore UI element position.");
                }
            }
        }
        else
        {
            Debug.Log("  Transition: uiElementToMove is NULL. Skipping UI element positioning.");
        }

        // 3. Vizuális Passthrough Állapot Váltása
        isPassthroughActive = enablePassthrough;
        Debug.Log($"  Transition: Internal state (isPassthroughActive) updated to: {isPassthroughActive}");

        passthroughLayer.enabled = isPassthroughActive;
        Debug.Log($"    OVRPassthroughLayer component '.enabled' set to: {passthroughLayer.enabled}");

        if (isPassthroughActive)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.clear;
            Debug.Log($"    Camera background set for Passthrough: ClearFlags={mainCamera.clearFlags}, BackgroundColor={mainCamera.backgroundColor}");
        }
        else
        {
            mainCamera.clearFlags = originalClearFlags;
            mainCamera.backgroundColor = originalBackgroundColor;
            Debug.Log($"    Camera background restored for VR: ClearFlags={mainCamera.clearFlags}, BackgroundColor={mainCamera.backgroundColor}");
        }

        virtualEnvironmentRoot.SetActive(!isPassthroughActive);
        Debug.Log($"    VirtualEnvironmentRoot GameObject '.SetActive()' called with: {!isPassthroughActive}");

        // 4. Fade In
        if (ovrScreenFade != null && fadeDuration > 0)
        {
            Debug.Log("  Transition: Fading in...");
            ovrScreenFade.fadeTime = fadeDuration;
            ovrScreenFade.FadeIn();
            yield return new WaitForSeconds(fadeDuration);
            Debug.Log("  Transition: Fade in complete.");
        }
        else
        {
            Debug.LogWarning("  Transition: OVRScreenFade not assigned or fadeDuration is 0. Skipping fade in.");
        }

        isTransitioning = false;
        Debug.Log($"--- Transition Finished. Passthrough State: {isPassthroughActive} --- Time: {Time.time}");
        Debug.Log($"===== SetPassthroughState (via coroutine) finished successfully for state: {isPassthroughActive} =====");
    }
}
