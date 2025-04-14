using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewSubjectConfig", menuName = "Configuration/Subject Config", order = 2)]
public class SubjectConfig : ScriptableObject
{
    [Tooltip("A tantárgy neve, ami megjelenik a felhasználói felületen.")]
    public string subjectName = "Új Tantárgy";

    [Tooltip("Az ehhez a tantárgyhoz tartozó témák konfigurációs fájljai.")]
    public List<TopicConfig> availableTopics = new List<TopicConfig>();
}
