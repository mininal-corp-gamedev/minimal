using Sandbox;
using System;

public sealed partial class Player
{
    private const float AlcoholShakeFrequency = 18f;
    private const float AlcoholMaxPixelateScale = 0.35f;

    [Property, Category( "Alcohol" )] public float MaxAlcoholBlood { get; set; } = 1f;
    [Property, Category( "Alcohol" )] public float AlcoholBloodDecayPerSecond { get; set; } = 0.025f;
    [Sync( SyncFlags.FromHost )] public float AlcoholBlood { get; private set; }

    private TimeUntil _alcoholEffectUntil;
    private float _alcoholEffectShakeStrength;
    private float _alcoholEffectPixelateScale;
    private Vector3 _alcoholBaseCameraOffset;
    private bool _alcoholCameraOffsetCaptured;
    private Pixelate _alcoholPixelate;

    private void StartLocalAlcoholEffect( float duration, float bloodFraction, float drinkPixelateScale, float drinkShakeStrength )
    {
        if ( IsProxy )
            return;

        duration = MathF.Max( 0f, duration );
        bloodFraction = Math.Clamp( bloodFraction, 0f, 1f );
        var bloodMultiplier = 0.65f + bloodFraction;

        _alcoholEffectUntil = MathF.Max( (float)_alcoholEffectUntil, duration );
        _alcoholEffectShakeStrength = MathF.Max( _alcoholEffectShakeStrength, drinkShakeStrength * bloodMultiplier );
        _alcoholEffectPixelateScale = MathF.Max( _alcoholEffectPixelateScale, drinkPixelateScale * bloodMultiplier );
    }

    private void UpdateLocalAlcoholEffect()
    {
        if ( IsProxy )
            return;

        if ( (float)_alcoholEffectUntil <= 0f )
        {
            RestoreLocalAlcoholEffect();
            return;
        }

        if ( Controller.IsValid() )
        {
            if ( !_alcoholCameraOffsetCaptured )
            {
                _alcoholBaseCameraOffset = Controller.CameraOffset;
                _alcoholCameraOffsetCaptured = true;
            }

            var time = Time.Now;
            var shake = new Vector3(
                MathF.Sin( time * AlcoholShakeFrequency * 1.37f ),
                MathF.Sin( time * AlcoholShakeFrequency * 1.91f + 1.1f ),
                MathF.Sin( time * AlcoholShakeFrequency * 1.53f + 2.4f ) ) * _alcoholEffectShakeStrength;

            Controller.CameraOffset = _alcoholBaseCameraOffset + shake;
        }

        var pixelate = GetAlcoholPixelate();
        if ( pixelate.IsValid() )
        {
            var scale = Math.Clamp( _alcoholEffectPixelateScale, 0f, AlcoholMaxPixelateScale );
            pixelate.Enabled = scale > 0.001f;
            pixelate.Scale = scale;
        }
    }

    private void RestoreLocalAlcoholEffect()
    {
        if ( _alcoholCameraOffsetCaptured && Controller.IsValid() )
            Controller.CameraOffset = _alcoholBaseCameraOffset;

        _alcoholCameraOffsetCaptured = false;
        _alcoholEffectShakeStrength = 0f;
        _alcoholEffectPixelateScale = 0f;

        if ( _alcoholPixelate.IsValid() )
        {
            _alcoholPixelate.Scale = 0f;
            _alcoholPixelate.Enabled = false;
        }
    }

    private Pixelate GetAlcoholPixelate()
    {
        if ( _alcoholPixelate.IsValid() )
            return _alcoholPixelate;

        var cameraObject = Scene?.Camera?.GameObject;
        if ( !cameraObject.IsValid() )
            return null;

        _alcoholPixelate = cameraObject.Components.Get<Pixelate>();
        if ( !_alcoholPixelate.IsValid() )
            _alcoholPixelate = cameraObject.Components.Create<Pixelate>();

        return _alcoholPixelate;
    }

    [Rpc.Broadcast]
    private static void RpcApplyAlcoholEffect( float duration, float bloodFraction, float drinkPixelateScale, float drinkShakeStrength )
    {
        Local?.StartLocalAlcoholEffect( duration, bloodFraction, drinkPixelateScale, drinkShakeStrength );
    }
}
