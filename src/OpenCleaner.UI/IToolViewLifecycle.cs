namespace OpenCleaner.UI;

/// <summary>
/// Optional lifecycle hook for tool views that retain heavy data while hidden.
/// The view instance stays alive; only its loaded data is released.
/// </summary>
internal interface IReleaseData
{
    void ReleaseData();
}
