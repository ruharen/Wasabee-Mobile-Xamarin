﻿using Rocks.Wasabee.Mobile.Core.Models.Operations;
using Xamarin.Forms.GoogleMaps;

namespace Rocks.Wasabee.Mobile.Core.ViewModels.Map
{
    public class WasabeePin
    {
        public Pin Pin { get; }

        public WasabeePin(Pin pin)
        {
            Pin = pin;
        }

        public PortalModel Portal { get; set; }
        public MarkerModel Marker { get; set; }
    }
}