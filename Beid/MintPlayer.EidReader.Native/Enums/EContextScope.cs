namespace MintPlayer.EidReader.Native.Enums;

public enum EContextScope : int
{
    SCARD_SCOPE_USER = 0, //Not for CE
    SCARD_SCOPE_TERMINAL = 1, //Not defined in doc
    SCARD_SCOPE_SYSTEM = 2
}
