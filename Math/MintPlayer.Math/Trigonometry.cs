namespace MintPlayer.Math;

public static class Trigonometry
{
    /// <summary>Returns the cosecant of the specified angle.</summary>
    /// <param name="d">An angle, measured in radians.</param>
    public static double Cosec(double d)
    {
        return 1 / System.Math.Sin(d);
    }

    /// <summary>Returns the secant of the specified angle.</summary>
    /// <param name="d">An angle, measured in radians.</param>
    public static double Sec(double d)
    {
        return 1 / System.Math.Cos(d);
    }

    /// <summary>Returns the cotangent of the specified angle.</summary>
    /// <param name="d">An angle, measured in radians.</param>
    public static double Cotan(double d)
    {
        return 1 / System.Math.Tan(d);
    }

    /// <summary>Returns the hyperbolic cosecant of the specified angle.</summary>
    /// <param name="d">An angle, measured in radians.</param>
    public static double CosecH(double d)
    {
        return 1 / System.Math.Sinh(d);
    }

    /// <summary>Returns the hyperbolic secant of the specified angle.</summary>
    /// <param name="d">An angle, measured in radians.</param>
    public static double SecH(double d)
    {
        return 1 / System.Math.Cosh(d);
    }

    /// <summary>Returns the hyperbolic cotangent of the specified angle.</summary>
    /// <param name="d">An angle, measured in radians.</param>
    public static double CotanH(double d)
    {
        return 1 / System.Math.Tanh(d);
    }

    /// <summary>Returns the angle whose cosecant is the specified number.</summary>
    /// <param name="d">A number representing a cosecant.</param>
    public static double Acosec(double d)
    {
        return System.Math.Asin(1 / d);
    }

    /// <summary>Returns the angle whose secant is the specified number.</summary>
    /// <param name="d">A number representing a secant.</param>
    public static double Asec(double d)
    {
        return System.Math.Acos(1 / d);
    }

    /// <summary>Returns the angle whose cotangent is the specified number.</summary>
    /// <param name="d">A number representing a cotangent.</param>
    public static double Acotan(double d)
    {
        return System.Math.Atan(1 / d);
    }

    /// <summary>Returns the angle whose hyperbolic sine is the specified number.</summary>
    /// <param name="d">A number representing a hyperbolic sine.</param>
    public static double AsinH(double d)
    {
        var asinh = System.Math.Log(d + System.Math.Sqrt(1 + System.Math.Pow(d, 2)));
        return asinh;
    }

    /// <summary>Returns the angle whose hyperbolic cosine is the specified number.</summary>
    /// <param name="d">A number representing a hyperbolic cosine.</param>
    public static double AcosH(double d)
    {
        var acosh = System.Math.Log(d + System.Math.Sqrt(d + 1) * System.Math.Sqrt(d - 1));
        return acosh;
    }

    /// <summary>Returns the angle whose hyperbolic tangent is the specified number.</summary>
    /// <param name="d">A number representing a hyperbolic tangent.</param>
    public static double AtanH(double d)
    {
        var atanh = System.Math.Log((1 + d) / (1 - d)) / 2;
        return atanh;
    }

    /// <summary>Returns the angle whose hyperbolic cosecant is the specified number.</summary>
    /// <param name="d">A number representing a hyperbolic cosecant.</param>
    public static double AcosecH(double d)
    {
        var acosech = System.Math.Log(1 / d + System.Math.Sqrt(1 / System.Math.Pow(d, 2) + 1));
        return acosech;
    }

    /// <summary>Returns the angle whose hyperbolic secant is the specified number.</summary>
    /// <param name="d">A number representing a hyperbolic secant.</param>
    public static double AsecH(double d)
    {
        var asech = System.Math.Log(1 / d + System.Math.Sqrt(1 / d + 1) * System.Math.Sqrt(1 / d - 1));
        return asech;
    }

    /// <summary>Returns the angle whose hyperbolic contangent is the specified number.</summary>
    /// <param name="d">A number representing a hyperbolic contangent.</param>
    public static double AcotanH(double d)
    {
        var acotanh = System.Math.Log((d + 1) / (d - 1)) / 2;
        return acotanh;
    }
}