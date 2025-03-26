using UnityEngine;
using UnityEngine.EventSystems;

public class EventSystemCreator : MonoBehaviour
{
    void Awake()
    {
        // Ellenőrizzük, van-e már EventSystem
        if (FindObjectOfType<EventSystem>() == null)
        {
            // Létrehozzuk az EventSystem GameObject-et
            GameObject eventSystem = new GameObject("EventSystem");

            // Hozzáadjuk a szükséges komponenseket
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            Debug.Log("EventSystem létrehozva programkódból");
        }
    }
}