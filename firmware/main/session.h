/*
 * The host side: the binary protocol (docs/development/protocol.md) over
 * UART0, which the DevKitC's "UART" USB port bridges to the host.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

/* Takes over UART0 at 921600 baud and starts the session task. */
void session_start(void);
