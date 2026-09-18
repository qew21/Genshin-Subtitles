using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;

namespace GI_Subtitles.Views
{
    public sealed class RegionPairSettings
    {
        private readonly LiveOverlaySession _session;
        private OverlayRect _addCapture = OverlayRect.Invalid;
        private OverlayRect _addDisplay = OverlayRect.Invalid;
        private int _selectedPairId;

        public RegionPairSettings(LiveOverlaySession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public IReadOnlyList<RegionPairCard> Cards
        {
            get { return Snapshot(); }
        }

        public bool IsEmpty
        {
            get { return _session.Pairs.Count == 0; }
        }

        public bool CanAdd
        {
            get { return !_session.AddInProgress && _session.Pairs.Count < LiveOverlaySession.SettingsPairCap; }
        }

        public int SelectedPairId
        {
            get { return _selectedPairId; }
        }

        public int VoicePrimaryOrdinal
        {
            get
            {
                int index = IndexOf(_session.VoicePrimaryId);
                return index < 0 ? 0 : index + 1;
            }
        }

        public int NextAddOrdinal
        {
            get { return _session.Pairs.Count + 1; }
        }

        public int OrdinalOf(int pairId)
        {
            int index = IndexOf(pairId);
            return index < 0 ? 0 : index + 1;
        }

        public void Select(int pairId)
        {
            _selectedPairId = IndexOf(pairId) < 0 ? 0 : pairId;
        }

        public bool TryStartAdd()
        {
            if (!_session.TryStartAdd())
            {
                return false;
            }

            _addCapture = OverlayRect.Invalid;
            _addDisplay = OverlayRect.Invalid;
            return true;
        }

        public void SetAddCapture(OverlayRect capture)
        {
            _addCapture = capture ?? OverlayRect.Invalid;
            _session.SetAddCapture(_addCapture);
        }

        public void SetAddDisplay(OverlayRect display)
        {
            _addDisplay = display ?? OverlayRect.Invalid;
            _session.SetAddDisplay(_addDisplay);
        }

        public void AbortAdd()
        {
            _session.AbortAdd();
            _addCapture = OverlayRect.Invalid;
            _addDisplay = OverlayRect.Invalid;
        }

        public bool TryCommitAdd()
        {
            if (!_session.AddInProgress)
            {
                return false;
            }

            if (!_addCapture.IsValid || !_addDisplay.IsValid)
            {
                AbortAdd();
                return false;
            }

            bool committed = _session.TryCommitAdd();
            if (committed && _session.Pairs.Count > 0)
            {
                _selectedPairId = _session.Pairs[_session.Pairs.Count - 1].Id;
            }
            else
            {
                AbortAdd();
            }

            return committed;
        }

        public bool TrySetCapture(int pairId, OverlayRect capture)
        {
            int index = IndexOf(pairId);
            if (index < 0 || capture == null || !capture.IsValid)
            {
                return false;
            }

            _session.SetCapture(index, capture);
            return true;
        }

        public bool TrySetDisplay(int pairId, OverlayRect display)
        {
            int index = IndexOf(pairId);
            if (index < 0 || display == null || !display.IsValid)
            {
                return false;
            }

            _session.SetDisplay(index, display);
            return true;
        }

        public void Delete(int pairId)
        {
            _session.DeletePair(pairId);
            if (IndexOf(_selectedPairId) < 0)
            {
                _selectedPairId = 0;
            }
        }

        public bool TryDesignate(int pairId)
        {
            if (pairId == _session.VoicePrimaryId || IndexOf(pairId) < 0)
            {
                return false;
            }

            _session.SetVoicePrimary(pairId);
            return true;
        }

        public bool TryToggleRegionAdjust(int pairId)
        {
            return _session.TryToggleRegionAdjust(pairId);
        }

        public void CancelRegionAdjust()
        {
            _session.CancelRegionAdjust();
        }

        public bool TryGetHotkeyTarget(out int pairIndex, out int pairId, out int ordinal)
        {
            pairIndex = -1;
            pairId = 0;
            ordinal = 0;
            if (_session.Pairs.Count == 0)
            {
                return false;
            }

            int selectedIndex = IndexOf(_selectedPairId);
            if (selectedIndex >= 0)
            {
                pairIndex = selectedIndex;
                pairId = _selectedPairId;
                ordinal = selectedIndex + 1;
                return true;
            }

            pairIndex = 0;
            pairId = _session.Pairs[0].Id;
            ordinal = 1;
            return true;
        }

        public bool TryBoxHotkeyPair(OverlayRect capture, OverlayRect display)
        {
            if (!TryGetHotkeyTarget(out int pairIndex, out _, out _))
            {
                return false;
            }

            if (capture == null || !capture.IsValid || display == null || !display.IsValid)
            {
                return false;
            }

            _session.SetCapture(pairIndex, capture);
            _session.SetDisplay(pairIndex, display);
            return true;
        }

        private List<RegionPairCard> Snapshot()
        {
            var cards = new List<RegionPairCard>(_session.Pairs.Count);
            for (int i = 0; i < _session.Pairs.Count; i++)
            {
                RegionPair pair = _session.Pairs[i];
                cards.Add(new RegionPairCard(
                    pair.Id,
                    i + 1,
                    pair.Capture,
                    pair.Display,
                    pair.Id == _selectedPairId,
                    pair.Id == _session.VoicePrimaryId,
                    pair.Id == _session.ArmedPairId,
                    i >= LiveOverlaySession.SettingsPairCap));
            }

            return cards;
        }

        private int IndexOf(int pairId)
        {
            if (pairId <= 0)
            {
                return -1;
            }

            for (int i = 0; i < _session.Pairs.Count; i++)
            {
                if (_session.Pairs[i].Id == pairId)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public sealed class RegionPairCard
    {
        public RegionPairCard(
            int id,
            int ordinal,
            OverlayRect capture,
            OverlayRect display,
            bool isSelected,
            bool isVoicePrimary,
            bool isAdjustArmed,
            bool isOverAddCap)
        {
            Id = id;
            Ordinal = ordinal;
            Capture = capture ?? OverlayRect.Invalid;
            Display = display ?? OverlayRect.Invalid;
            IsSelected = isSelected;
            IsVoicePrimary = isVoicePrimary;
            IsAdjustArmed = isAdjustArmed;
            IsOverAddCap = isOverAddCap;
        }

        public int Id { get; }

        public int Ordinal { get; }

        public OverlayRect Capture { get; }

        public OverlayRect Display { get; }

        public bool IsSelected { get; }

        public bool IsVoicePrimary { get; }

        public bool IsAdjustArmed { get; }

        public bool CanAdjustRegion
        {
            get { return Capture.IsValid || Display.IsValid; }
        }

        public bool IsOverAddCap { get; }

        public string CaptureText
        {
            get { return Format(Capture); }
        }

        public string DisplayText
        {
            get { return Format(Display); }
        }

        private static string Format(OverlayRect rect)
        {
            if (rect == null || !rect.IsValid)
            {
                return string.Empty;
            }

            return rect.X + ", " + rect.Y + ", " + rect.Width + ", " + rect.Height;
        }
    }
}
