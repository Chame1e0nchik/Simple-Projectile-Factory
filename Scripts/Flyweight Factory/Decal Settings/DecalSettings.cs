using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SimpleProjectileFactory
{
    [CreateAssetMenu(fileName = "SimpleProjectileFactory", menuName = "Spells/Decal Settings", order = 1)]
    public class DecalSettings : FlyweightSettings
    {
        public float despawnTime;
        public Material decalMaterial;
        public float fadeDuration;
        public float fadeFactor;
        public float fadeScale;
        public Vector3 decalSize;

        Coroutine fadeOut;

        public override Flyweight Create()
        {
            var go = Instantiate(prefab);
            go.SetActive(false);
            go.name = prefab.name;

            var decal = go.GetOrAdd<Decal>();

            decal.settings = this;
            return decal;
        }

        public void SpawnDecal(RaycastHit hit)
        {
            var decal = flyweightFactory.Spawn(this) as Decal;

            DecalProjector projector = decal.GetComponent<DecalProjector>();

            // DecalProjector projector = decalPool.Get();

            projector.transform.position = hit.point + hit.normal * 0.01f;
            
            Quaternion normalRotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
            Quaternion randomRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            projector.transform.rotation = normalRotation * randomRotation;

            projector.size = decalSize;

            // fadeOut = StartCoroutine(FadeAndRelease(projector, despawnTime));
        }

        IEnumerator FadeAndRelease(DecalProjector projector, float duration)
        {
            float time = 0f;
            float initialFade = projector.fadeFactor;

            while (time < duration)
            {
                if (projector == null) yield break;
                
                time += Time.deltaTime;
                float t = time / duration;
                projector.fadeFactor = Mathf.Lerp(initialFade, 0f, t);
                yield return null;
            }

            // if (projector != null)
            // {
            //     projector.fadeFactor = initialFade;
            //     decalPool.Release(projector);
            // }
        }
    }
}