// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
// SPDX-License-Identifier: AGPL-3.0-or-later

function resolveApiUrl() {
    const host = window.location.hostname;
    if (host === 'localhost' || host === '127.0.0.1') return 'http://localhost:5023';
    if (host.includes('testingmachine')) return 'https://geo.api.opendatahub.testingmachine.eu';
    return 'https://geo.api.opendatahub.com';
}

const CONFIG = {
    apiUrl: resolveApiUrl(),
    defaultCenter: [11.35, 46.5],
    defaultZoom: 10
};