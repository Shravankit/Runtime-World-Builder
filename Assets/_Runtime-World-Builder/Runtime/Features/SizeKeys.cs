using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Brush / stamp size is changed ONLY with + and - (scroll wheel and trackpad scroll are camera zoom).
    /// Use it in any tool:   size *= SizeKeys.Factor(Time.deltaTime);
    public static class SizeKeys
    {
        /// Multiplier for this frame: above 1 while + (or =) is held, below 1 while - is held, otherwise exactly 1.
        /// With the default speed the size doubles (or halves) after one second of holding the key.
        public static float Factor(float deltaTime, float doublesPerSecond = 1f)
        {
            bool plus = Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus) || Input.GetKey(KeyCode.KeypadPlus);
            bool minus = Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus);
            if (plus == minus) return 1f;
            return Mathf.Pow(2f, (plus ? 1f : -1f) * doublesPerSecond * deltaTime);
        }
    }
}