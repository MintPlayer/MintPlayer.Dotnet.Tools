using MintPlayer.Math;

namespace MintPlayer.Math.Tests;

/// <summary>
/// Known-answer tests. Every expectation is an independently-derived value (a textbook
/// identity or the reciprocal/inverse relationship), never a value copied from this
/// implementation's own output — otherwise the test would only pin current behaviour.
/// </summary>
public class TrigonometryTests
{
    private const double Tolerance = 1e-12;

    #region Reciprocal functions

    [Theory]
    // cosec(pi/6) = 1/sin(pi/6) = 1/0.5 = 2
    [InlineData(System.Math.PI / 6, 2d)]
    // cosec(pi/2) = 1/1 = 1
    [InlineData(System.Math.PI / 2, 1d)]
    public void Cosec_ReturnsReciprocalOfSine(double radians, double expected)
        => Trigonometry.Cosec(radians).Should().BeCloseTo(expected, Tolerance);

    [Theory]
    // sec(0) = 1/cos(0) = 1
    [InlineData(0d, 1d)]
    // sec(pi/3) = 1/0.5 = 2
    [InlineData(System.Math.PI / 3, 2d)]
    public void Sec_ReturnsReciprocalOfCosine(double radians, double expected)
        => Trigonometry.Sec(radians).Should().BeCloseTo(expected, Tolerance);

    [Theory]
    // cotan(pi/4) = 1/tan(pi/4) = 1
    [InlineData(System.Math.PI / 4, 1d)]
    // cotan(pi/3) = 1/sqrt(3)
    [InlineData(System.Math.PI / 3, 0.5773502691896257d)]
    public void Cotan_ReturnsReciprocalOfTangent(double radians, double expected)
        => Trigonometry.Cotan(radians).Should().BeCloseTo(expected, Tolerance);

    [Fact]
    public void Cosec_AtZero_IsPositiveInfinity()
        => double.IsPositiveInfinity(Trigonometry.Cosec(0)).Should().BeTrue();

    [Fact]
    public void Cotan_AtZero_IsPositiveInfinity()
        => double.IsPositiveInfinity(Trigonometry.Cotan(0)).Should().BeTrue();

    [Fact]
    public void Sec_AtHalfPi_IsEnormous_BecauseCosineIsNotExactlyZero()
    {
        // cos(pi/2) is ~6.1e-17 in double precision, not 0, so this is a very large
        // finite number rather than infinity. Documented because it is surprising.
        var result = Trigonometry.Sec(System.Math.PI / 2);
        double.IsInfinity(result).Should().BeFalse();
        System.Math.Abs(result).Should().BeGreaterThan(1e15);
    }

    #endregion

    #region Hyperbolic reciprocal functions

    [Fact]
    // cosech(x) = 1/sinh(x); sinh(1) = 1.1752011936438014
    public void CosecH_ReturnsReciprocalOfSinh()
        => Trigonometry.CosecH(1d).Should().BeCloseTo(1d / System.Math.Sinh(1d), Tolerance);

    [Fact]
    // sech(0) = 1/cosh(0) = 1/1 = 1
    public void SecH_AtZero_IsOne()
        => Trigonometry.SecH(0d).Should().BeCloseTo(1d, Tolerance);

    [Fact]
    // coth(x) = 1/tanh(x), and tanh -> 1 as x grows, so coth -> 1 from above
    public void CotanH_ForLargeInput_ApproachesOneFromAbove()
    {
        var result = Trigonometry.CotanH(10d);
        result.Should().BeGreaterThan(1d);
        result.Should().BeCloseTo(1d, 1e-8);
    }

    [Fact]
    public void CosecH_AtZero_IsPositiveInfinity()
        => double.IsPositiveInfinity(Trigonometry.CosecH(0)).Should().BeTrue();

    [Fact]
    public void CotanH_AtZero_IsPositiveInfinity()
        => double.IsPositiveInfinity(Trigonometry.CotanH(0)).Should().BeTrue();

    #endregion

    #region Inverse reciprocal functions - verified by round-tripping

    [Theory]
    [InlineData(2d)]
    [InlineData(5d)]
    [InlineData(1.5d)]
    public void Acosec_IsInverseOfCosec(double value)
        => Trigonometry.Cosec(Trigonometry.Acosec(value)).Should().BeCloseTo(value, 1e-10);

    [Theory]
    [InlineData(2d)]
    [InlineData(5d)]
    [InlineData(1.5d)]
    public void Asec_IsInverseOfSec(double value)
        => Trigonometry.Sec(Trigonometry.Asec(value)).Should().BeCloseTo(value, 1e-10);

    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    [InlineData(0.5d)]
    public void Acotan_IsInverseOfCotan(double value)
        => Trigonometry.Cotan(Trigonometry.Acotan(value)).Should().BeCloseTo(value, 1e-10);

    [Fact]
    // asec(1) = acos(1) = 0
    public void Asec_AtOne_IsZero()
        => Trigonometry.Asec(1d).Should().BeCloseTo(0d, Tolerance);

    [Fact]
    // acosec(1) = asin(1) = pi/2
    public void Acosec_AtOne_IsHalfPi()
        => Trigonometry.Acosec(1d).Should().BeCloseTo(System.Math.PI / 2, Tolerance);

    [Fact]
    // 1/d for d in (-1,1) is outside asin's domain, so the result is NaN
    public void Acosec_InsideTheUnitInterval_IsNaN()
        => double.IsNaN(Trigonometry.Acosec(0.5d)).Should().BeTrue();

    #endregion

    #region Inverse hyperbolic functions

    [Theory]
    // asinh(0) = 0
    [InlineData(0d, 0d)]
    // asinh(1) = ln(1 + sqrt(2))
    [InlineData(1d, 0.8813735870195430d)]
    // asinh is odd, so asinh(-1) = -asinh(1)
    [InlineData(-1d, -0.8813735870195430d)]
    public void AsinH_MatchesKnownValues(double value, double expected)
        => Trigonometry.AsinH(value).Should().BeCloseTo(expected, 1e-10);

    [Fact]
    public void AsinH_AgreesWithTheBcl()
        => Trigonometry.AsinH(3.7d).Should().BeCloseTo(System.Math.Asinh(3.7d), 1e-12);

    [Theory]
    // acosh(1) = 0
    [InlineData(1d, 0d)]
    // acosh(2) = ln(2 + sqrt(3))
    [InlineData(2d, 1.3169578969248166d)]
    public void AcosH_MatchesKnownValues(double value, double expected)
        => Trigonometry.AcosH(value).Should().BeCloseTo(expected, 1e-10);

    [Fact]
    public void AcosH_AgreesWithTheBcl()
        => Trigonometry.AcosH(4.2d).Should().BeCloseTo(System.Math.Acosh(4.2d), 1e-12);

    [Fact]
    // acosh is undefined below 1
    public void AcosH_BelowOne_IsNaN()
        => double.IsNaN(Trigonometry.AcosH(0.5d)).Should().BeTrue();

    [Theory]
    // atanh(0) = 0
    [InlineData(0d, 0d)]
    // atanh(0.5) = 0.5 * ln(3)
    [InlineData(0.5d, 0.5493061443340549d)]
    [InlineData(-0.5d, -0.5493061443340549d)]
    public void AtanH_MatchesKnownValues(double value, double expected)
        => Trigonometry.AtanH(value).Should().BeCloseTo(expected, 1e-10);

    [Fact]
    public void AtanH_AgreesWithTheBcl()
        => Trigonometry.AtanH(0.75d).Should().BeCloseTo(System.Math.Atanh(0.75d), 1e-12);

    [Fact]
    public void AtanH_AtOne_IsPositiveInfinity()
        => double.IsPositiveInfinity(Trigonometry.AtanH(1d)).Should().BeTrue();

    [Fact]
    // acsch(x) = asinh(1/x), so acsch(1) = asinh(1)
    public void AcosecH_AtOne_EqualsAsinhOfOne()
        => Trigonometry.AcosecH(1d).Should().BeCloseTo(System.Math.Asinh(1d), 1e-12);

    [Fact]
    public void AcosecH_IsInverseOfCosecH()
        => Trigonometry.CosecH(Trigonometry.AcosecH(2.5d)).Should().BeCloseTo(2.5d, 1e-10);

    [Fact]
    // asech(x) = acosh(1/x), so asech(1) = acosh(1) = 0
    public void AsecH_AtOne_IsZero()
        => Trigonometry.AsecH(1d).Should().BeCloseTo(0d, 1e-12);

    [Fact]
    public void AsecH_IsInverseOfSecH()
        => Trigonometry.SecH(Trigonometry.AsecH(0.4d)).Should().BeCloseTo(0.4d, 1e-10);

    [Fact]
    // asech is undefined outside (0, 1]
    public void AsecH_AboveOne_IsNaN()
        => double.IsNaN(Trigonometry.AsecH(2d)).Should().BeTrue();

    [Fact]
    // acoth(x) = 0.5 * ln((x+1)/(x-1)), so acoth(2) = 0.5 * ln(3)
    public void AcotanH_MatchesKnownValue()
        => Trigonometry.AcotanH(2d).Should().BeCloseTo(0.5493061443340549d, 1e-10);

    [Fact]
    public void AcotanH_IsInverseOfCotanH()
        => Trigonometry.CotanH(Trigonometry.AcotanH(3d)).Should().BeCloseTo(3d, 1e-10);

    [Fact]
    public void AcotanH_AtOne_IsPositiveInfinity()
        => double.IsPositiveInfinity(Trigonometry.AcotanH(1d)).Should().BeTrue();

    [Fact]
    // acoth is undefined on (-1, 1)
    public void AcotanH_InsideTheUnitInterval_IsNaN()
        => double.IsNaN(Trigonometry.AcotanH(0.5d)).Should().BeTrue();

    #endregion

    #region NaN propagation

    [Fact]
    public void EveryFunction_PropagatesNaN()
    {
        double.IsNaN(Trigonometry.Cosec(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.Sec(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.Cotan(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.CosecH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.SecH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.CotanH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.Acosec(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.Asec(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.Acotan(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.AsinH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.AcosH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.AtanH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.AcosecH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.AsecH(double.NaN)).Should().BeTrue();
        double.IsNaN(Trigonometry.AcotanH(double.NaN)).Should().BeTrue();
    }

    #endregion
}
