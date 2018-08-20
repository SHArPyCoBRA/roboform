// Copyright (C) 2018 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;

namespace OPVault
{
    public class BaseException: Exception
    {
        public BaseException(string message): base(message)
        {
        }

        public BaseException(string message, Exception innerException):
            base(message, innerException)
        {
        }
    }
}
