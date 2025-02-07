﻿// <copyright file="Element.cs" company="The Android Open Source Project, Ryan Conrad, Quamotion, yungd1plomat, wherewhere">
// Copyright (c) The Android Open Source Project, Ryan Conrad, Quamotion, yungd1plomat, wherewhere. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AdvancedSharpAdbClient
{
    /// <summary>
    /// Implement of screen element, likes Selenium.
    /// </summary>
    public class Element
    {
        /// <summary>
        /// The current ADB client that manages the connection.
        /// </summary>
        private IAdbClient Client { get; set; }

        /// <summary>
        /// The current device containing the element.
        /// </summary>
        private DeviceData Device { get; set; }

        /// <summary>
        /// Contains element coordinates.
        /// </summary>
        public Cords Cords { get; set; }

        /// <summary>
        /// Gets or sets element attributes.
        /// </summary>
        public Dictionary<string, string> Attributes { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Element"/> class.
        /// </summary>
        /// <param name="client">The current ADB client that manages the connection.</param>
        /// <param name="device">The current device containing the element.</param>
        /// <param name="cords">Contains element coordinates .</param>
        /// <param name="attributes">Gets or sets element attributes.</param>
        public Element(IAdbClient client, DeviceData device, Cords cords, Dictionary<string, string> attributes)
        {
            Client = client;
            Device = device;
            Cords = cords;
            Attributes = attributes;
        }

        /// <summary>
        /// Clicks on this coordinates.
        /// </summary>
        public void Click() => Client.Click(Device, Cords);

        /// <summary>
        /// Clicks on this coordinates.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> which can be used to cancel the asynchronous operation.</param>
        public async Task ClickAsync(CancellationToken cancellationToken = default) =>
            await Client.ClickAsync(Device, Cords, cancellationToken);

        /// <summary>
        /// Send text to device. Doesn't support Russian.
        /// </summary>
        /// <param name="text"></param>
        public void SendText(string text)
        {
            Click();
            Client.SendText(Device, text);
        }

        /// <summary>
        /// Clear the input text. The input should be in focus. Use el.ClearInput() if the element isn't focused.
        /// </summary>
        /// <param name="charCount"></param>
        public void ClearInput(int charCount = 0)
        {
            Click(); // focuses
            if (charCount == 0)
            {
                Client.ClearInput(Device, Attributes["text"].Length);
            }
            else
            {
                Client.ClearInput(Device, charCount);
            }
        }
    }
}
