using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewSubjectConfig", menuName = "Configuration/Subject Config", order = 2)]
public class SubjectConfig : ScriptableObject
{
    [Tooltip("A tant�rgy neve, ami megjelenik a felhaszn�l�i fel�leten.")]
    public string subjectName = "�j Tant�rgy";

    [Tooltip("Az ehhez a tant�rgyhoz tartoz� t�m�k konfigur�ci�s f�jljai.")]
    public List<TopicConfig> availableTopics = new List<TopicConfig>();
}
