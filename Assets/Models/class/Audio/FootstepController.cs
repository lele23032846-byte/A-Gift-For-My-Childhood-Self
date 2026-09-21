using UnityEngine;

public class FootstepController : MonoBehaviour
{
    public AudioSource audioSource;

    public AudioClip grassFootstep;
    public AudioClip stoneFootstep;
    public AudioClip woodFootstep;

    public enum GroundType
    {
        Grass,
        Stone,
        Wood
    }

    public GroundType currentGround = GroundType.Grass;

    public void PlayFootstep()
    {
        AudioClip clip = grassFootstep;

        if (currentGround == GroundType.Stone)
            clip = stoneFootstep;
        else if (currentGround == GroundType.Wood)
            clip = woodFootstep;

        audioSource.pitch = Random.Range(0.9f, 1.1f);
        audioSource.PlayOneShot(
            clip,
            Random.Range(0.3f, 0.6f)
        );
    }
}