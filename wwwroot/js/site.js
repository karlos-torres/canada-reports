(() => {
    const dashboard = document.getElementById("report-dashboard");
    if (!dashboard) {
        return;
    }

    const selectedReportKey = dashboard.dataset.selectedReportKey || "";
    const runButton = document.getElementById("run-report-btn");
    const refreshButton = document.getElementById("refresh-runs-btn");
    const runLoading = document.getElementById("run-loading");
    const runFeedback = document.getElementById("run-feedback");
    const recentRunsBody = document.getElementById("recent-runs-body");
    const notificationsList = document.getElementById("notifications-list");
    const latestRunEmpty = document.getElementById("latest-run-empty");
    const latestRunDetails = document.getElementById("latest-run-details");
    const latestRunName = document.getElementById("latest-run-name");
    const latestRunStatus = document.getElementById("latest-run-status");
    const latestRunMeta = document.getElementById("latest-run-meta");
    const latestRunStage = document.getElementById("latest-run-stage");
    const cancelRunButton = document.getElementById("cancel-run-btn");

    let currentRunId = null;
    let pollHandle = null;

    const statusClassMap = {
        Queued: "text-bg-secondary",
        Running: "text-bg-primary",
        Completed: "text-bg-success",
        Failed: "text-bg-danger",
        Cancelled: "text-bg-dark"
    };

    const showFeedback = (message, type = "info") => {
        if (!runFeedback) {
            return;
        }

        runFeedback.className = `alert alert-${type}`;
        runFeedback.classList.remove("d-none");
        runFeedback.textContent = message;
    };

    const clearFeedback = () => {
        if (!runFeedback) {
            return;
        }

        runFeedback.classList.add("d-none");
        runFeedback.textContent = "";
    };

    const setLoading = (isLoading) => {
        runButton?.toggleAttribute("disabled", isLoading || !selectedReportKey);
        if (runLoading) {
            runLoading.classList.toggle("d-none", !isLoading);
        }
    };

    const formatDate = (value) => {
        if (!value) {
            return "-";
        }

        return new Date(value).toLocaleString();
    };

    const formatDuration = (seconds) => {
        if (seconds == null) {
            return "-";
        }

        return `${seconds.toFixed(2)}s`;
    };

    const renderLatestRun = (run) => {
        if (!run || !latestRunDetails || !latestRunEmpty) {
            return;
        }

        latestRunEmpty.classList.add("d-none");
        latestRunDetails.classList.remove("d-none");
        latestRunName.textContent = run.reportName;
        latestRunStatus.textContent = run.status;
        latestRunStatus.className = `badge ${statusClassMap[run.status] || "text-bg-secondary"}`;
        latestRunMeta.textContent = `Requested ${formatDate(run.requestedAtUtc)} · Attempts ${run.attemptCount}`;
        latestRunStage.textContent = run.hasRealProgress
            ? `${run.stageText} (${run.progressPercent}%)`
            : run.stageText;

        if (cancelRunButton) {
            cancelRunButton.classList.toggle("d-none", !run.canCancel);
            cancelRunButton.dataset.runId = run.id;
        }
    };

    const renderNotifications = (runs) => {
        if (!notificationsList) {
            return;
        }

        const notices = runs
            .filter((run) => run.notificationMessage)
            .slice(0, 8);

        if (notices.length === 0) {
            notificationsList.innerHTML = `<li class="list-group-item text-muted">No notifications yet.</li>`;
            return;
        }

        notificationsList.innerHTML = notices
            .map((run) => `<li class="list-group-item"><strong>${run.reportName}</strong>: ${run.notificationMessage}</li>`)
            .join("");
    };

    const renderRecentRuns = (runs) => {
        if (!recentRunsBody) {
            return;
        }

        if (runs.length === 0) {
            recentRunsBody.innerHTML = `<tr><td colspan="5" class="text-muted">No runs yet.</td></tr>`;
            return;
        }

        recentRunsBody.innerHTML = runs
            .map((run) => {
                const downloadLink = run.canDownload
                    ? `<a class="btn btn-sm btn-outline-success" href="/api/report-runs/${run.id}/download">Download CSV</a>`
                    : "";
                const cancelButton = run.canCancel
                    ? `<button type="button" class="btn btn-sm btn-outline-danger cancel-run-inline" data-run-id="${run.id}">Cancel</button>`
                    : "";

                return `
<tr>
<td>${run.reportName}</td>
<td><div class="small text-muted">${run.stageText}</div></td>
<td>${formatDate(run.requestedAtUtc)}</td>
<td>${formatDuration(run.durationSeconds)}</td>
<td class="d-flex gap-2 flex-wrap">${downloadLink}${cancelButton}</td>
</tr>`;
            })
            .join("");
    };

    const fetchRuns = async () => {
        const response = await fetch("/api/report-runs?limit=25", { method: "GET" });
        if (!response.ok) {
            throw new Error("Unable to load report runs.");
        }

        const runs = await response.json();
        renderRecentRuns(runs);
        renderNotifications(runs);
        renderLatestRun(runs[0]);
        return runs;
    };

    const queueRun = async () => {
        clearFeedback();
        setLoading(true);

        try {
            const response = await fetch("/api/report-runs", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ reportKey: selectedReportKey })
            });

            if (!response.ok) {
                throw new Error("Failed to start report run.");
            }

            const run = await response.json();
            currentRunId = run.id;
            showFeedback(`Run ${run.id} queued.`, "success");
            await fetchRuns();
            startPolling();
        } catch (error) {
            showFeedback(error.message || "Unable to queue report run.", "danger");
        } finally {
            setLoading(false);
        }
    };

    const cancelRun = async (runId) => {
        if (!runId) {
            return;
        }

        const response = await fetch(`/api/report-runs/${runId}/cancel`, { method: "POST" });
        if (!response.ok) {
            showFeedback("Unable to cancel run.", "warning");
            return;
        }

        showFeedback("Run cancelled.", "warning");
        await fetchRuns();
    };

    const startPolling = () => {
        if (pollHandle) {
            return;
        }

        pollHandle = window.setInterval(async () => {
            try {
                const runs = await fetchRuns();
                const activeRuns = runs.filter((run) => run.canCancel);
                if (activeRuns.length === 0 && pollHandle) {
                    window.clearInterval(pollHandle);
                    pollHandle = null;
                }
            } catch {
                // keep polling loop resilient
            }
        }, 4000);
    };

    runButton?.addEventListener("click", queueRun);
    refreshButton?.addEventListener("click", async () => {
        try {
            await fetchRuns();
            clearFeedback();
        } catch {
            showFeedback("Unable to refresh runs.", "danger");
        }
    });

    cancelRunButton?.addEventListener("click", async () => {
        await cancelRun(cancelRunButton.dataset.runId);
    });

    recentRunsBody?.addEventListener("click", async (event) => {
        const button = event.target.closest(".cancel-run-inline");
        if (!button) {
            return;
        }

        await cancelRun(button.dataset.runId);
    });

    fetchRuns().then((runs) => {
        if (runs.some((run) => run.canCancel)) {
            startPolling();
        }
    }).catch(() => {
        showFeedback("Unable to load report runs.", "danger");
    });
})();
