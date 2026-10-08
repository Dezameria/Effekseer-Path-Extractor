#if !NETFRAMEWORK
using Effekseer.Data;
using Effekseer.Data.Value;

namespace EffekseerAI.Host;

// A typed, bounded recipe for 1.80.7. All children belong to a new group;
// applying/undoing it does not modify existing nodes or shared assets.
internal static class TornadoRecipe
{
    public static void Create(NodeBase parent, string textures)
    {
        var group = parent.AddChild();
        group.Name.SetValue("Tempest - Raging Tornado");
        group.IsRendered.SetValue(false);
        group.CommonValues.Life.SetCenter(480);
        for (var level = 0; level < 8; level++)
        {
            float height = level * 1.15f;
            float radius = 0.45f + level * 0.34f;
            var cloud = Layer(group, $"Funnel cloud {level + 1:00}", textures, "cloud.png",
                radius, height, 1.2f + level * 0.22f, 0.65f, 85, 1.65f - level * 0.055f);
            cloud.DrawingValues.ColorAll.Type.SetValue(StandardColorType.Random);
            var tint = cloud.DrawingValues.ColorAll.Random;
            tint.R.SetCenter(82 + level * 3); tint.R.SetAmplitude(18);
            tint.G.SetCenter(104 + level * 3); tint.G.SetAmplitude(18);
            tint.B.SetCenter(131 + level * 3); tint.B.SetAmplitude(18);
            tint.A.SetCenter(75); tint.A.SetAmplitude(24);
            cloud.LocationValues.PVA.Velocity.Y.SetCenter(0.024f);
            cloud.LocationAbsValues.LocalForceField2.Type.SetValue(LocalForceFieldType.Turbulence);
            cloud.LocationAbsValues.LocalForceField2.Power.SetValue(0.025f);
            cloud.LocationAbsValues.LocalForceField2.Turbulence.FieldScale.SetValue(2.5f);
            cloud.LocationAbsValues.LocalForceField2.Turbulence.Seed.SetValue(41 + level);
            if (level % 2 == 0)
            {
                var wind = Layer(group, $"Fast wind ribbons {level / 2 + 1}", textures, "wind.png",
                    radius + 0.35f, height + 0.3f, 2.6f + level * 0.23f, 2.0f, 46, 2.3f);
                wind.DrawingValues.ColorAll.Fixed.SetValue(145, 209, 255, 110);
                wind.RendererCommonValues.AlphaBlend.SetValue(AlphaBlendType.Add);
                wind.LocationValues.PVA.Velocity.Y.SetCenter(0.052f);
                wind.RotationValues.PVA.Rotation.Z.SetAmplitude(20);
                wind.RotationValues.PVA.Velocity.Z.SetCenter(1.0f);
            }
        }
        var dust = Layer(group, "Ground dust surge", textures, "cloud.png", 2.8f, 0.18f, 2.3f, 0.8f, 58, 1.2f);
        dust.DrawingValues.ColorAll.Fixed.SetValue(117, 112, 100, 80);
        dust.LocationValues.PVA.Velocity.Y.SetCenter(0.009f);
        dust.GenerationLocationValues.Circle.Radius.SetAmplitude(1.4f);
        var debris = Layer(group, "Debris lifted in the vortex", textures, "spark.png", 1.2f, 0.1f, 0.18f, 1.0f, 125, 2.0f);
        debris.DrawingValues.ColorAll.Fixed.SetValue(163, 149, 121, 255);
        debris.LocationValues.PVA.Velocity.Y.SetCenter(0.083f);
        debris.LocationValues.PVA.Velocity.Y.SetAmplitude(0.027f);
        debris.GenerationLocationValues.Circle.Radius.SetAmplitude(0.8f);
        var sparks = Layer(group, "Charged storm flecks", textures, "spark.png", 3.0f, 3.0f, 0.12f, 2.0f, 72, 2.6f);
        sparks.DrawingValues.ColorAll.Fixed.SetValue(142, 214, 255, 240);
        sparks.RendererCommonValues.AlphaBlend.SetValue(AlphaBlendType.Add);
        sparks.LocationValues.PVA.Location.Y.SetAmplitude(3.0f);
        sparks.LocationValues.PVA.Velocity.Y.SetCenter(0.075f);
    }

    private static Node Layer(Node parent, string name, string textures, string texture,
        float radius, float height, float size, float interval, int lifetime, float vortex)
    {
        var n = parent.AddChild();
        n.Name.SetValue(name);
        n.CommonValues.MaxGeneration.Value.SetValue((int)(360 / interval));
        n.CommonValues.Life.SetCenter(lifetime);
        n.CommonValues.Life.SetAmplitude(lifetime / 6);
        n.CommonValues.Generation.GenerationTime.SetCenter(interval);
        n.GenerationLocationValues.Type.SetValue(GenerationLocationValues.ParameterType.Circle);
        n.GenerationLocationValues.Circle.AxisDirection.SetValue(AxisType.YAxis);
        n.GenerationLocationValues.Circle.Type.SetValue(GenerationLocationValues.CircleType.Random);
        n.GenerationLocationValues.Circle.Radius.SetCenter(radius);
        n.GenerationLocationValues.Circle.Radius.SetAmplitude(radius * 0.12f);
        n.GenerationLocationValues.Circle.AngleEnd.SetCenter(360);
        n.GenerationLocationValues.EffectsRotation.SetValue(false);
        n.LocationValues.Type.SetValue(LocationValues.ParamaterType.PVA);
        n.LocationValues.PVA.Location.Y.SetCenter(height);
        n.LocationValues.PVA.Location.Y.SetAmplitude(0.22f);
        n.RotationValues.Type.SetValue(RotationValues.ParamaterType.PVA);
        n.RotationValues.PVA.Rotation.Z.SetAmplitude(180);
        n.RotationValues.PVA.Velocity.Z.SetCenter(1.4f);
        n.RotationValues.PVA.Velocity.Z.SetAmplitude(0.8f);
        n.ScalingValues.Type.SetValue(ScaleValues.ParamaterType.PVA);
        Set(n.ScalingValues.PVA.Scale, size, size, size);
        Set(n.ScalingValues.PVA.Velocity, 0.008f, 0.008f, 0.008f);
        n.LocationAbsValues.LocalForceField1.Type.SetValue(LocalForceFieldType.Vortex);
        n.LocationAbsValues.LocalForceField1.Vortex.VortexType.SetValue(ForceFieldVortexType.ConstantAngle);
        n.LocationAbsValues.LocalForceField1.Power.SetValue(vortex);
        n.RendererCommonValues.ColorTexture.SetAbsolutePath(System.IO.Path.Combine(textures, texture));
        n.RendererCommonValues.ZWrite.SetValue(false);
        n.RendererCommonValues.FadeInType.SetValue(RendererCommonValues.FadeInMethod.Use);
        n.RendererCommonValues.FadeIn.Frame.SetValue(10);
        n.RendererCommonValues.FadeOutType.SetValue(RendererCommonValues.FadeOutMethod.WithinLifetime);
        n.RendererCommonValues.FadeOut.Frame.SetValue(22);
        return n;
    }

    private static void Set(Vector3DWithRandom value, float x, float y, float z)
    {
        value.X.SetCenter(x); value.Y.SetCenter(y); value.Z.SetCenter(z);
    }
}
#endif
