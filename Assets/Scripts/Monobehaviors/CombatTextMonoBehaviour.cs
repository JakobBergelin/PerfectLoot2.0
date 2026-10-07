using TMPro;
using Unity.Entities;
using UnityEngine;

public class CombatTextMonoBehaviour : MonoBehaviour
{
    public string Text;

    public Color Color;
    public GameObject TextPrefab;

    public float Duration;
    public Vector3 Direction;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        TextPrefab.GetComponent<TextMeshProUGUI>().SetText(Text);
        TextPrefab.GetComponent<TextMeshProUGUI>().color = Color;
    }

    // Update is called once per frame
    void Update()
    {
        gameObject.transform.position += Direction * Time.deltaTime * 0.7f;
        Duration -= Time.deltaTime;
        if(Duration <= 0.0f)
        {
            Destroy(TextPrefab);
            Destroy(this.gameObject);
        }
    }
}
