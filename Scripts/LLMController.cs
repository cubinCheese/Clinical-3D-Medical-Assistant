using UnityEngine;


public class LLMController : MonoBehaviour
{
    public LLMUnity.LLMCharacter llmCharacter;
    public TMPro.TMP_InputField playerText;
    public TMPro.TMP_Text aiText;
    public UnityEngine.UI.Button submit;

    void Start()
    {
        submit.onClick.AddListener(OnSubmitButtonClick); 

    }
    public void OnSubmitButtonClick()
    {
        // map player text input to LLM Character (for processing and response)
        llmCharacter.Chat(playerText.text, HandleReply); 
    }

    // display LLM Character reply to UI
    private void HandleReply(string reply)
    {
        Debug.Log(reply);
        aiText.text = reply;
    }
}
 