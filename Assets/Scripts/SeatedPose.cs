using UnityEngine;

// Sits the (Humanoid) mini-me at his desk with IK: feet on the floor, knees forward, hands on the keyboard.
// Sits on the Animator's GameObject. While he waves or talks the hands let go of the keyboard.
[RequireComponent(typeof(Animator))]
public class SeatedPose : MonoBehaviour
{
    public Transform footL, footR, kneeL, kneeR, handL, handR, elbowL, elbowR, lookAt;
    public Transform player;          // the little character hopping through the game on the screen
    public Material codeMat;          // the scrolling code on the screen
    public Vector2 playerRange;       // local x range of the hop, and the floor y (x = min, y = max)
    public float playerFloor, hop = 0.006f;
    [HideInInspector] public float typing = 1f;
    [HideInInspector] public float seated = 1f; // 0 lets go of the chair, floor and keyboard (the parachute ride)
    float typePhase;

    Animator anim;
    float left = 1, right = 1;
    Vector3 handLRest, handRRest;
    Vector3 playerStart;

    void Awake()
    {
        anim = GetComponent<Animator>();
        handLRest = handL.localPosition;
        handRRest = handR.localPosition;
        if (player) playerStart = player.localPosition;
    }

    void Update()
    {
        if (codeMat) codeMat.mainTextureOffset = new Vector2(0, Time.time * 0.12f % 1f);
        if (player)
        {
            float x = Mathf.Lerp(playerRange.x, playerRange.y, Mathf.PingPong(Time.time * 0.35f, 1f));
            player.localPosition = new Vector3(x, playerFloor + Mathf.Abs(Mathf.Sin(Time.time * 5f)) * hop, playerStart.z);
        }

        // typing: the hands tap in turn (faster as `typing` goes up; Bug Hunt speeds him up)
        typePhase += Time.deltaTime * 21f * typing;
        handL.localPosition = handLRest + Vector3.up * Mathf.Max(0, Mathf.Sin(typePhase)) * 0.003f;
        handR.localPosition = handRRest + Vector3.up * Mathf.Max(0, Mathf.Sin(typePhase + 2.4f)) * 0.003f;

        var info = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
        float l = 1, r = 1;
        if (info.IsName("Wave")) { r = 0; l = 0.6f; }
        else if (info.IsName("Talk")) l = r = 0.25f;
        else if (info.IsName("Cheer")) l = r = 0;
        left = Mathf.MoveTowards(left, l, Time.unscaledDeltaTime * 4f);
        right = Mathf.MoveTowards(right, r, Time.unscaledDeltaTime * 4f);
    }

    void OnAnimatorIK(int layer)
    {
        Goal(AvatarIKGoal.LeftFoot, footL, seated, true);
        Goal(AvatarIKGoal.RightFoot, footR, seated, true);
        Hint(AvatarIKHint.LeftKnee, kneeL, seated);
        Hint(AvatarIKHint.RightKnee, kneeR, seated);
        Goal(AvatarIKGoal.LeftHand, handL, left * seated, false);
        Goal(AvatarIKGoal.RightHand, handR, right * seated, false);
        Hint(AvatarIKHint.LeftElbow, elbowL, left * seated);
        Hint(AvatarIKHint.RightElbow, elbowR, right * seated);
        anim.SetLookAtWeight(0.6f, 0.2f, 0.8f);
        anim.SetLookAtPosition(lookAt.position);
    }

    void Goal(AvatarIKGoal goal, Transform t, float weight, bool rotate)
    {
        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKPosition(goal, t.position);
        anim.SetIKRotationWeight(goal, rotate ? weight : 0);
        if (rotate) anim.SetIKRotation(goal, t.rotation);
    }

    void Hint(AvatarIKHint hint, Transform t, float weight)
    {
        anim.SetIKHintPositionWeight(hint, weight);
        anim.SetIKHintPosition(hint, t.position);
    }
}
