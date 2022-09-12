// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP3_0_OR_GREATER && !NET6_0_OR_GREATER
#define HIDE_MESSAGELESS_IMPLEMENTATION
#endif

namespace Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/// <summary>
/// A collection of helper classes to test various conditions within
/// unit tests. If the condition being tested is not met, an exception
/// is thrown.
/// </summary>
public sealed partial class Assert
{
#if HIDE_MESSAGELESS_IMPLEMENTATION
    /// <summary>
    /// Tests whether the specified object is an instance of the expected
    /// type and throws an exception if the expected type is not in the
    /// inheritance hierarchy of the object.
    /// </summary>
    /// <param name="value">
    /// The object the test expects to be of the specified type.
    /// </param>
    /// <param name="expectedType">
    /// The expected type of <paramref name="value"/>.
    /// </param>
    /// <exception cref="AssertFailedException">
    /// Thrown if <paramref name="value"/> is null or
    /// <paramref name="expectedType"/> is not in the inheritance hierarchy
    /// of <paramref name="value"/>.
    /// </exception>
    public static void IsInstanceOfType(object value, Type expectedType)
    {
        IsInstanceOfType(value, expectedType, string.Empty, null);
    }
#endif

    /// <summary>
    /// Tests whether the specified object is an instance of the expected
    /// type and throws an exception if the expected type is not in the
    /// inheritance hierarchy of the object.
    /// </summary>
    /// <param name="value">
    /// The object the test expects to be of the specified type.
    /// </param>
    /// <param name="expectedType">
    /// The expected type of <paramref name="value"/>.
    /// </param>
    /// <param name="message">
    /// The message to include in the exception when <paramref name="value"/>
    /// is not an instance of <paramref name="expectedType"/>. The message is
    /// shown in test results.
    /// </param>
    /// <exception cref="AssertFailedException">
    /// Thrown if <paramref name="value"/> is null or
    /// <paramref name="expectedType"/> is not in the inheritance hierarchy
    /// of <paramref name="value"/>.
    /// </exception>
    public static void IsInstanceOfType(object value, Type expectedType, [CallerArgumentExpression("value")] string message = null)
    {
        IsInstanceOfType(value, expectedType, message, null);
    }

    /// <summary>
    /// Tests whether the specified object is an instance of the expected
    /// type and throws an exception if the expected type is not in the
    /// inheritance hierarchy of the object.
    /// </summary>
    /// <param name="value">
    /// The object the test expects to be of the specified type.
    /// </param>
    /// <param name="expectedType">
    /// The expected type of <paramref name="value"/>.
    /// </param>
    /// <param name="message">
    /// The message to include in the exception when <paramref name="value"/>
    /// is not an instance of <paramref name="expectedType"/>. The message is
    /// shown in test results.
    /// </param>
    /// <param name="parameters">
    /// An array of parameters to use when formatting <paramref name="message"/>.
    /// </param>
    /// <exception cref="AssertFailedException">
    /// Thrown if <paramref name="value"/> is null or
    /// <paramref name="expectedType"/> is not in the inheritance hierarchy
    /// of <paramref name="value"/>.
    /// </exception>
    public static void IsInstanceOfType(object value, Type expectedType, [CallerArgumentExpression("value")] string message = null, params object[] parameters)
    {
        if (expectedType == null || value == null)
        {
            ThrowAssertFailed("Assert.IsInstanceOfType", BuildUserMessage(message, parameters));
        }

        var elementTypeInfo = value.GetType().GetTypeInfo();
        var expectedTypeInfo = expectedType.GetTypeInfo();
        if (!expectedTypeInfo.IsAssignableFrom(elementTypeInfo))
        {
            string userMessage = BuildUserMessage(message, parameters);
            string finalMessage = string.Format(
                CultureInfo.CurrentCulture,
                FrameworkMessages.IsInstanceOfFailMsg,
                userMessage,
                expectedType.ToString(),
                value.GetType().ToString());
            ThrowAssertFailed("Assert.IsInstanceOfType", finalMessage);
        }
    }

#if HIDE_MESSAGELESS_IMPLEMENTATION
    /// <summary>
    /// Tests whether the specified object is not an instance of the wrong
    /// type and throws an exception if the specified type is in the
    /// inheritance hierarchy of the object.
    /// </summary>
    /// <param name="value">
    /// The object the test expects not to be of the specified type.
    /// </param>
    /// <param name="wrongType">
    /// The type that <paramref name="value"/> should not be.
    /// </param>
    /// <exception cref="AssertFailedException">
    /// Thrown if <paramref name="value"/> is not null and
    /// <paramref name="wrongType"/> is in the inheritance hierarchy
    /// of <paramref name="value"/>.
    /// </exception>
    public static void IsNotInstanceOfType(object value, Type wrongType)
    {
        IsNotInstanceOfType(value, wrongType, string.Empty, null);
    }
#endif

    /// <summary>
    /// Tests whether the specified object is not an instance of the wrong
    /// type and throws an exception if the specified type is in the
    /// inheritance hierarchy of the object.
    /// </summary>
    /// <param name="value">
    /// The object the test expects not to be of the specified type.
    /// </param>
    /// <param name="wrongType">
    /// The type that <paramref name="value"/> should not be.
    /// </param>
    /// <param name="message">
    /// The message to include in the exception when <paramref name="value"/>
    /// is an instance of <paramref name="wrongType"/>. The message is shown
    /// in test results.
    /// </param>
    /// <exception cref="AssertFailedException">
    /// Thrown if <paramref name="value"/> is not null and
    /// <paramref name="wrongType"/> is in the inheritance hierarchy
    /// of <paramref name="value"/>.
    /// </exception>
    public static void IsNotInstanceOfType(object value, Type wrongType, [CallerArgumentExpression("value")] string message = null)
    {
        IsNotInstanceOfType(value, wrongType, message, null);
    }

    /// <summary>
    /// Tests whether the specified object is not an instance of the wrong
    /// type and throws an exception if the specified type is in the
    /// inheritance hierarchy of the object.
    /// </summary>
    /// <param name="value">
    /// The object the test expects not to be of the specified type.
    /// </param>
    /// <param name="wrongType">
    /// The type that <paramref name="value"/> should not be.
    /// </param>
    /// <param name="message">
    /// The message to include in the exception when <paramref name="value"/>
    /// is an instance of <paramref name="wrongType"/>. The message is shown
    /// in test results.
    /// </param>
    /// <param name="parameters">
    /// An array of parameters to use when formatting <paramref name="message"/>.
    /// </param>
    /// <exception cref="AssertFailedException">
    /// Thrown if <paramref name="value"/> is not null and
    /// <paramref name="wrongType"/> is in the inheritance hierarchy
    /// of <paramref name="value"/>.
    /// </exception>
    public static void IsNotInstanceOfType(object value, Type wrongType, [CallerArgumentExpression("value")] string message = null, params object[] parameters)
    {
        if (wrongType == null)
        {
            ThrowAssertFailed("Assert.IsNotInstanceOfType", BuildUserMessage(message, parameters));
        }

        // Null is not an instance of any type.
        if (value == null)
        {
            return;
        }

        var elementTypeInfo = value.GetType().GetTypeInfo();
        var expectedTypeInfo = wrongType.GetTypeInfo();
        if (expectedTypeInfo.IsAssignableFrom(elementTypeInfo))
        {
            string userMessage = BuildUserMessage(message, parameters);
            string finalMessage = string.Format(
                CultureInfo.CurrentCulture,
                FrameworkMessages.IsNotInstanceOfFailMsg,
                userMessage,
                wrongType.ToString(),
                value.GetType().ToString());
            ThrowAssertFailed("Assert.IsNotInstanceOfType", finalMessage);
        }
    }
}