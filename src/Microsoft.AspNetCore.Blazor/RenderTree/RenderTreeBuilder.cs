﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Blazor.Components;
using Microsoft.AspNetCore.Blazor.Rendering;
using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Blazor.RenderTree
{
    // IMPORTANT
    //
    // Many of these names are used in code generation. Keep these in sync with the code generation code
    // See: src/Microsoft.AspNetCore.Blazor.Razor.Extensions/RenderTreeBuilder.cs

    /// <summary>
    /// Provides methods for building a collection of <see cref="RenderTreeFrame"/> entries.
    /// </summary>
    public class RenderTreeBuilder
    {
        private readonly Renderer _renderer;
        private readonly ArrayBuilder<RenderTreeFrame> _entries = new ArrayBuilder<RenderTreeFrame>(10);
        private readonly Stack<int> _openElementIndices = new Stack<int>();
        private RenderTreeFrameType? _lastNonAttributeFrameType;

        /// <summary>
        /// Constructs an instance of <see cref="RenderTreeBuilder"/>.
        /// </summary>
        /// <param name="renderer">The associated <see cref="Renderer"/>.</param>
        public RenderTreeBuilder(Renderer renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        /// <summary>
        /// Appends a frame representing an element, i.e., a container for other frames.
        /// In order for the <see cref="RenderTreeBuilder"/> state to be valid, you must
        /// also call <see cref="CloseElement"/> immediately after appending the
        /// new element's child frames.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="elementName">A value representing the type of the element.</param>
        public void OpenElement(int sequence, string elementName)
        {
            _openElementIndices.Push(_entries.Count);
            Append(RenderTreeFrame.Element(sequence, elementName));
        }

        /// <summary>
        /// Marks a previously appended element frame as closed. Calls to this method
        /// must be balanced with calls to <see cref="OpenElement(string)"/>.
        /// </summary>
        public void CloseElement()
        {
            var indexOfEntryBeingClosed = _openElementIndices.Pop();
            ref var entry = ref _entries.Buffer[indexOfEntryBeingClosed];
            entry = entry.WithElementSubtreeLength(_entries.Count - indexOfEntryBeingClosed);
        }

        /// <summary>
        /// Appends a frame representing text content.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="textContent">Content for the new text frame.</param>
        public void AddText(int sequence, string textContent)
            => Append(RenderTreeFrame.Text(sequence, textContent ?? string.Empty));

        /// <summary>
        /// Appends a frame representing text content.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="textContent">Content for the new text frame.</param>
        public void AddText(int sequence, object textContent)
            => AddText(sequence, textContent?.ToString());

        /// <summary>
        /// Appends a frame representing a string-valued attribute.
        /// The attribute is associated with the most recently added element.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="name">The name of the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        public void AddAttribute(int sequence, string name, string value)
        {
            AssertCanAddAttribute();
            Append(RenderTreeFrame.Attribute(sequence, name, value));
        }

        /// <summary>
        /// Appends a frame representing an <see cref="UIEventArgs"/>-valued attribute.
        /// The attribute is associated with the most recently added element.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="name">The name of the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        public void AddAttribute(int sequence, string name, UIEventHandler value)
        {
            AssertCanAddAttribute();
            Append(RenderTreeFrame.Attribute(sequence, name, value));
        }

        /// <summary>
        /// Appends a frame representing a string-valued attribute.
        /// The attribute is associated with the most recently added element.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="name">The name of the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        public void AddAttribute(int sequence, string name, object value)
        {
            if (_lastNonAttributeFrameType == RenderTreeFrameType.Element)
            {
                // Element attribute values can only be strings or UIEventHandler
                Append(RenderTreeFrame.Attribute(sequence, name, value.ToString()));
            }
            else if (_lastNonAttributeFrameType == RenderTreeFrameType.Component)
            {
                Append(RenderTreeFrame.Attribute(sequence, name, value));
            }
            else
            {
                // This is going to throw. Calling it just to get a consistent exception message.
                AssertCanAddAttribute();
            }
        }

        /// <summary>
        /// Appends a frame representing an attribute.
        /// The attribute is associated with the most recently added element.
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        /// <param name="name">The name of the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        public void AddAttribute(int sequence, RenderTreeFrame frame)
        {
            if (frame.FrameType != RenderTreeFrameType.Attribute)
            {
                throw new ArgumentException($"The {nameof(frame.FrameType)} must be {RenderTreeFrameType.Attribute}.");
            }

            AssertCanAddAttribute();
            Append(frame.WithAttributeSequence(sequence));
        }

        /// <summary>
        /// Appends a frame representing a child component.
        /// </summary>
        /// <typeparam name="TComponent">The type of the child component.</typeparam>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        public void OpenComponent<TComponent>(int sequence) where TComponent : IComponent
        {
            // Currently, child components can't have further grandchildren of their own, so it would
            // technically be possible to skip their CloseElement calls and not track them in _openElementIndices.
            // However at some point we might want to have the grandchildren frames available at runtime
            // (rather than being parsed as attributes at compile time) so that we could have APIs for
            // components to query the complete hierarchy of transcluded frames instead of forcing the
            // transcluded subtree to be in a particular shape such as representing key/value pairs.
            // So it's more flexible if we track open/close frames for components explicitly.
            _openElementIndices.Push(_entries.Count);
            Append(RenderTreeFrame.ChildComponent<TComponent>(sequence));
        }

        /// <summary>
        /// Marks a previously appended component frame as closed. Calls to this method
        /// must be balanced with calls to <see cref="OpenComponent{TComponent}"/>.
        /// </summary>
        public void CloseComponent()
        {
            var indexOfEntryBeingClosed = _openElementIndices.Pop();
            ref var entry = ref _entries.Buffer[indexOfEntryBeingClosed];
            entry = entry.WithComponentSubtreeLength(_entries.Count - indexOfEntryBeingClosed);
        }

        /// <summary>
        /// Appends a frame denoting the start of a region (that is, a tree fragment that is
        /// processed as a unit for the purposes of diffing).
        /// </summary>
        /// <param name="sequence">An integer that represents the position of the instruction in the source code.</param>
        public void OpenRegion(int sequence)
        {
            _openElementIndices.Push(_entries.Count);
            Append(RenderTreeFrame.Region(sequence));
        }

        /// <summary>
        /// Marks a previously appended region frame as closed. Calls to this method
        /// must be balanced with calls to <see cref="OpenRegion"/>.
        /// </summary>
        public void CloseRegion()
        {
            var indexOfEntryBeingClosed = _openElementIndices.Pop();
            ref var entry = ref _entries.Buffer[indexOfEntryBeingClosed];
            entry = entry.WithRegionSubtreeLength(_entries.Count - indexOfEntryBeingClosed);
        }

        private void AssertCanAddAttribute()
        {
            if (_lastNonAttributeFrameType != RenderTreeFrameType.Element
                && _lastNonAttributeFrameType != RenderTreeFrameType.Component)
            {
                throw new InvalidOperationException($"Attributes may only be added immediately after frames of type {RenderTreeFrameType.Element} or {RenderTreeFrameType.Component}");
            }
        }

        /// <summary>
        /// Clears the builder.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            _openElementIndices.Clear();
            _lastNonAttributeFrameType = null;
        }

        /// <summary>
        /// Returns the <see cref="RenderTreeFrame"/> values that have been appended.
        /// </summary>
        /// <returns>An array range of <see cref="RenderTreeFrame"/> values.</returns>
        public ArrayRange<RenderTreeFrame> GetFrames() =>
            _entries.ToRange();

        private void Append(in RenderTreeFrame frame)
        {
            _entries.Append(frame);

            var frameType = frame.FrameType;
            if (frameType != RenderTreeFrameType.Attribute)
            {
                _lastNonAttributeFrameType = frame.FrameType;
            }
        }
    }
}
