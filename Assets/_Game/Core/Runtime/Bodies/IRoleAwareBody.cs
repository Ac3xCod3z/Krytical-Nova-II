namespace KryticalNova.Core
{
    /// <summary>
    /// Marker: this component asks <see cref="NetRole"/> what it is and acts on the answer (a ported system). Every other
    /// component of a body built by the entity netcode runs as the offline avatar would, i.e. as a local stub (decision D2 of
    /// M3a-2b), so a system joins the real network by implementing this marker together with its port.
    /// </summary>
    public interface IRoleAwareBody
    {
    }
}
