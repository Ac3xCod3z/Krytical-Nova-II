using PSD.Creator.Data;
using PSD.Creator.Objects;
using PSD.Creator.Tool;

namespace PSD.Creator.Components
{
    public abstract class ParticleComponent<T, S> : PSDComponent<T, S>
        where T : Particle<S>
        where S : ParticleObject
    {
        protected abstract S Object { get; }

        protected override void ResetSystem()
        {
            System.SetFloat("_Cutoff", Object.cutoff);
            System.SetFloat("_SoftParticles", Object.softParticles);
            System.SetFloat("_DitherFade", (BasicSettings.useDitherFade && Object.useDitherFade) ? 1 : 0);
            System.SetFloat("_Dither", BasicSettings.ditherFade * Object.ditherFade);
            System.SetTexture("_Albedo", Object.albedo);
            System.SetVector("_Flipbook", Object.flipbookSize);
            System.SetTexture("_Normal", Object.normal);
            System.SetFloat("_NormalScale", Object.normalStrength);
            System.SetTexture("_Dissolve", Object.dissolve);
        }
    }
}